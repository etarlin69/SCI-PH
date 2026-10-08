using Microsoft.EntityFrameworkCore;
using Npgsql;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Patients;
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
            ["IX_ClinicalEncounters_TemporaryPatientCode"] = "The temporary patient code is already in use; retry the operation."
        };

        private readonly ClinicalDbContext _clinical;

        public ClinicalWriteStore(ClinicalDbContext clinical) => _clinical = clinical;

        public Task<ClinicalEncounter?> FindEncounterAsync(Guid encounterId, CancellationToken cancellationToken) =>
            _clinical.ClinicalEncounters.SingleOrDefaultAsync(e => e.Id == encounterId, cancellationToken);

        public void Add(Patient patient) => _clinical.Patients.Add(patient);

        public void Add(ClinicalEncounter encounter) => _clinical.ClinicalEncounters.Add(encounter);

        public void Add(Triage triage) => _clinical.Triages.Add(triage);

        public void Add(VitalSign vitalSign) => _clinical.VitalSigns.Add(vitalSign);

        public void Add(ClinicalAuditEntry auditEntry) => _clinical.ClinicalAuditEntries.Add(auditEntry);

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
                ConstraintName: { } constraint
            } && _knownUniqueViolations.TryGetValue(constraint, out var message))
            {
                _clinical.ChangeTracker.Clear();
                throw new ResourceConflictException(message, exception);
            }
        }
    }
}
