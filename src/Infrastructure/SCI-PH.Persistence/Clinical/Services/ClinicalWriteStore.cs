using Microsoft.EntityFrameworkCore;
using Npgsql;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Entities.Transport;
using SCI_PH.Persistence.Configurations.Clinical.Biometrics;
using SCI_PH.Persistence.Configurations.Clinical.Idempotency;
using SCI_PH.Persistence.Configurations.Clinical.Patients;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.Persistence.Clinical.Services
{
    // One SaveChanges per use case = one transaction. ClinicalEncounter.PatientId and Status are concurrency tokens,
    // so a concurrent identification or status change makes the second writer fail instead of overwriting.
    internal sealed class ClinicalWriteStore : IClinicalWriteStore
    {
        // Unique indexes whose violation is an expected conflict rather than a server error.
        private static readonly IReadOnlyDictionary<string, string> _knownUniqueViolations = new Dictionary<string, string>
        {
            ["UX_PatientIdentifiers_ActiveValue"] = "An active identifier with this value already belongs to another patient.",
            ["IX_ClinicalEncounters_TemporaryPatientCode"] = "The temporary patient code is already in use; retry the operation.",
            ["UX_ClinicalTransfers_ActivePerEncounter"] = "The encounter already has an active transfer.",
            [FingerprintIdentificationSessionConfiguration.ActivePerEncounterIndexName] = "The emergency already has an active fingerprint session.",
            [FingerprintIdentificationSessionConfiguration.PendingPerReaderIndexName] = "The fingerprint reader is busy with another session.",
            [FingerprintEnrollmentSessionConfiguration.PendingPerPatientIndexName] = "The patient already has a pending fingerprint enrollment.",
            [FingerprintEnrollmentSessionConfiguration.PendingPerReaderIndexName] = "The fingerprint reader is busy with another enrollment.",
            [PatientBiometricReferenceConfiguration.ActiveSubjectIndexName] = "The biometric reference is already active.",
            [PatientBiometricReferenceConfiguration.ActiveFacePerPatientIndexName] = "The patient already has an active face reference.",
            [FingerprintSlotDeletionJobConfiguration.PerReferenceIndexName] = "The biometric reference already has a deletion job.",
            [FaceTemplateDeletionJobConfiguration.PerReferenceIndexName] = "The face reference already has a template deletion job."
        };

        private readonly ClinicalDbContext _clinical;

        public ClinicalWriteStore(ClinicalDbContext clinical) => _clinical = clinical;

        public Task<ClinicalEncounter?> FindEncounterAsync(Guid encounterId, CancellationToken cancellationToken) =>
            _clinical.ClinicalEncounters.SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken);

        public void Add(Patient patient) => _clinical.Patients.Add(patient);

        public void Add(ClinicalEncounter encounter) => _clinical.ClinicalEncounters.Add(encounter);

        public void Add(EncounterStaffAssignment assignment) => _clinical.EncounterStaffAssignments.Add(assignment);

        public void Add(Triage triage) => _clinical.Triages.Add(triage);

        public void Add(VitalSign vitalSign) => _clinical.VitalSigns.Add(vitalSign);

        public void Add(ClinicalNote note) => _clinical.ClinicalNotes.Add(note);

        public void Add(Diagnosis diagnosis) => _clinical.Diagnoses.Add(diagnosis);

        public void Add(ClinicalProcedure procedure) => _clinical.ClinicalProcedures.Add(procedure);

        // Its details are inserted with it (same graph, same transaction).
        public void Add(Prescription prescription) => _clinical.Prescriptions.Add(prescription);

        public void Add(MedicationAdministration administration) => _clinical.MedicationAdministrations.Add(administration);

        public Task<ClinicalTransfer?> FindTransferAsync(Guid transferId, CancellationToken cancellationToken) =>
            _clinical.ClinicalTransfers.SingleOrDefaultAsync(t => t.Id == transferId, cancellationToken);

        public void Add(ClinicalTransfer transfer) => _clinical.ClinicalTransfers.Add(transfer);

        public void Add(IdentificationAttempt attempt) => _clinical.IdentificationAttempts.Add(attempt);

        public void Add(PatientBiometricReference reference) => _clinical.PatientBiometricReferences.Add(reference);

        public Task<PatientBiometricReference?> FindBiometricReferenceAsync(Guid referenceId, CancellationToken cancellationToken) =>
            _clinical.PatientBiometricReferences.SingleOrDefaultAsync(r => r.Id == referenceId, cancellationToken);

        // Same mechanism as EnsureEncounterUnchanged: UPDATE ... SET "Status" = "Status" WHERE "Id" = ... AND "Status" =
        // original, re-evaluated by PostgreSQL against the latest committed row, so a revocation committed meanwhile makes
        // it match no row and the whole save rolls back.
        public void EnsureBiometricReferenceUnchanged(PatientBiometricReference reference) =>
            _clinical.Entry(reference).Property(r => r.Status).IsModified = true;

        public void Add(ClinicalAuditEntry auditEntry) => _clinical.ClinicalAuditEntries.Add(auditEntry);

        // Marks Status as modified (same value) so SaveChanges issues UPDATE ... WHERE "Status" = original AND
        // "PatientId" = original in the same transaction as the inserted record. PostgreSQL re-evaluates that WHERE
        // against the latest committed row, so a concurrent close makes it match no row and the whole save rolls back.
        public void EnsureEncounterUnchanged(ClinicalEncounter encounter) =>
            _clinical.Entry(encounter).Property(e => e.Status).IsModified = true;

        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _clinical.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                _clinical.ChangeTracker.Clear();
                throw new ResourceConflictException("The record was changed by another operation. Reload it and try again.", exception);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: IdempotencyReceiptConfiguration.UniqueKeyName
            })
            {
                // Another request with the same idempotency key committed first; this transaction was rolled back.
                _clinical.ChangeTracker.Clear();
                throw new IdempotencyKeyCollisionException(exception);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: { } constraint
            } && _knownUniqueViolations.TryGetValue(constraint, out var message))
            {
                _clinical.ChangeTracker.Clear();
                throw new ResourceConflictException(message, exception);
            }
        }
    }
}
