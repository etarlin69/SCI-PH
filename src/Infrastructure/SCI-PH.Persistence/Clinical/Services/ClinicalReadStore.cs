using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.Enums.Transport;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.Persistence.Clinical.Services
{
    internal sealed class ClinicalReadStore : IClinicalReadStore
    {
        // Primary active identifier first, then the oldest active one. A QR credential is never a display identifier: its
        // value is a hash of a system-issued token, and it must not reach a summary even masked (ADR-015).
        private static readonly Expression<Func<Patient, PatientRow>> _patientRow = p => new PatientRow(
            p.Id,
            p.GivenNames,
            p.FamilyNames,
            p.DateOfBirth,
            p.Sex,
            p.IdentityStatus,
            p.Identifiers
                .Where(i => i.Status == PatientIdentifierStatus.Active && i.Type != PatientIdentifierType.QrCode)
                .OrderByDescending(i => i.IsPrimary)
                .ThenBy(i => i.CreatedAtUtc)
                .Select(i => new PatientIdentifierRow(i.Type, i.NormalizedValue))
                .FirstOrDefault());

        private static readonly Expression<Func<Medication, MedicationSummary>> _medication = m => new MedicationSummary(
            m.Id, m.Code, m.Name, m.Presentation, m.Concentration, m.DefaultRoute, m.Status);

        // Details have no entry order of their own: they are listed by medication name.
        private static readonly Expression<Func<Prescription, PrescriptionSummary>> _prescription = p => new PrescriptionSummary(
            p.Id,
            p.ClinicalEncounterId,
            p.Status,
            p.Notes,
            p.CreatedAtUtc,
            p.IssuedAtUtc,
            p.ValidUntilUtc,
            p.CancelledAtUtc,
            p.PrescribedByStaffId,
            p.Details
                .OrderBy(d => d.Medication.Name)
                .Select(d => new PrescriptionDetailSummary(
                    d.Id, d.MedicationId, d.Medication.Name, d.Dose, d.DoseUnit, d.Route, d.Frequency, d.DurationDays, d.Instructions))
                .ToList());

        private readonly ClinicalDbContext _clinical;

        public ClinicalReadStore(ClinicalDbContext clinical) => _clinical = clinical;

        public Task<bool> PatientExistsAsync(Guid patientId, CancellationToken cancellationToken) =>
            _clinical.Patients.AnyAsync(p => p.Id == patientId, cancellationToken);

        public Task<bool> ActiveIdentifierExistsAsync(
            PatientIdentifierType type, string issuer, string normalizedValue, CancellationToken cancellationToken) =>
            _clinical.PatientIdentifiers.AnyAsync(
                i => i.Type == type && i.Issuer == issuer && i.NormalizedValue == normalizedValue && i.Status == PatientIdentifierStatus.Active,
                cancellationToken);

        public Task<PatientRow?> GetPatientAsync(Guid patientId, CancellationToken cancellationToken) =>
            _clinical.Patients.AsNoTracking().Where(p => p.Id == patientId).Select(_patientRow).SingleOrDefaultAsync(cancellationToken);

        public async Task<IReadOnlyList<PatientRow>> FindPatientsByIdentifierAsync(
            PatientIdentifierType type, string normalizedValue, int limit, CancellationToken cancellationToken) =>
            await _clinical.Patients.AsNoTracking()
                .Where(p => p.Identifiers.Any(i =>
                    i.Type == type && i.NormalizedValue == normalizedValue && i.Status == PatientIdentifierStatus.Active))
                .OrderBy(p => p.FamilyNames).ThenBy(p => p.GivenNames)
                .Take(limit)
                .Select(_patientRow)
                .ToListAsync(cancellationToken);

        public Task<PatientIdentifierMatch?> FindPlatformIdentifierAsync(
            PatientIdentifierType type, string normalizedValue, CancellationToken cancellationToken) =>
            _clinical.PatientIdentifiers.AsNoTracking()
                .Where(i => i.Type == type && i.Issuer == string.Empty && i.NormalizedValue == normalizedValue
                    && i.Status == PatientIdentifierStatus.Active)
                .Select(i => new PatientIdentifierMatch(i.Id, i.PatientId))
                .SingleOrDefaultAsync(cancellationToken);

        // Case-insensitive "contains" over the full name. Contains is translated with escaped LIKE wildcards.
        public async Task<IReadOnlyList<PatientRow>> FindPatientsByNameAsync(string term, int limit, CancellationToken cancellationToken)
        {
            var lowered = term.ToLowerInvariant();
            return await _clinical.Patients.AsNoTracking()
                .Where(p => (p.GivenNames + " " + p.FamilyNames).ToLower().Contains(lowered))
                .OrderBy(p => p.FamilyNames).ThenBy(p => p.GivenNames)
                .Take(limit)
                .Select(_patientRow)
                .ToListAsync(cancellationToken);
        }

        public Task<BiometricReferenceRow?> FindActiveFingerprintReferenceAsync(
            string provider, string subjectReference, CancellationToken cancellationToken) =>
            _clinical.PatientBiometricReferences.AsNoTracking()
                .Where(r => r.Modality == BiometricModality.Fingerprint
                    && r.Provider == provider
                    && r.SubjectReference == subjectReference
                    && r.Status == BiometricReferenceStatus.Active)
                .Select(r => new BiometricReferenceRow(r.Id, r.PatientId))
                .SingleOrDefaultAsync(cancellationToken);

        public Task<Guid?> GetActiveBiometricReferencePatientIdAsync(Guid biometricReferenceId, CancellationToken cancellationToken) =>
            _clinical.PatientBiometricReferences.AsNoTracking()
                .Where(r => r.Id == biometricReferenceId && r.Status == BiometricReferenceStatus.Active)
                .Select(r => (Guid?)r.PatientId)
                .SingleOrDefaultAsync(cancellationToken);

        public Task<bool> HasActiveFingerprintReferenceAsync(
            Guid patientId, string provider, string subjectReferencePrefix, CancellationToken cancellationToken) =>
            _clinical.PatientBiometricReferences.AnyAsync(r => r.PatientId == patientId
                && r.Modality == BiometricModality.Fingerprint
                && r.Provider == provider
                && r.Status == BiometricReferenceStatus.Active
                && r.SubjectReference.StartsWith(subjectReferencePrefix), cancellationToken);

        public async Task<IReadOnlyList<string>> ListActiveFingerprintSubjectReferencesAsync(
            string provider, string subjectReferencePrefix, CancellationToken cancellationToken) =>
            await _clinical.PatientBiometricReferences.AsNoTracking()
                .Where(r => r.Modality == BiometricModality.Fingerprint
                    && r.Provider == provider
                    && r.Status == BiometricReferenceStatus.Active
                    && r.SubjectReference.StartsWith(subjectReferencePrefix))
                .Select(r => r.SubjectReference)
                .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<FingerprintReferenceRow>> ListFingerprintReferencesAsync(Guid patientId, CancellationToken cancellationToken) =>
            await _clinical.PatientBiometricReferences.AsNoTracking()
                .Where(r => r.PatientId == patientId && r.Modality == BiometricModality.Fingerprint)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Select(r => new FingerprintReferenceRow(r.Id, r.Provider, r.SubjectReference, r.Status, r.CreatedAtUtc, r.RevokedAtUtc))
                .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<FaceReferenceRow>> ListFaceReferencesAsync(Guid patientId, CancellationToken cancellationToken) =>
            await FaceReferences(patientId)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Select(FaceRow)
                .ToListAsync(cancellationToken);

        public Task<FaceReferenceRow?> FindActiveFaceReferenceAsync(Guid patientId, CancellationToken cancellationToken) =>
            FaceReferences(patientId)
                .Where(r => r.Status == BiometricReferenceStatus.Active)
                .Select(FaceRow)
                .SingleOrDefaultAsync(cancellationToken);

        public Task<string?> GetAuditChangesAsync(Guid auditEntryId, string entityName, Guid patientId, CancellationToken cancellationToken) =>
            _clinical.ClinicalAuditEntries.AsNoTracking()
                .Where(a => a.Id == auditEntryId && a.EntityName == entityName && a.PatientId == patientId)
                .Select(a => a.ChangesJson)
                .SingleOrDefaultAsync(cancellationToken);

        private IQueryable<PatientBiometricReference> FaceReferences(Guid patientId) =>
            _clinical.PatientBiometricReferences.AsNoTracking()
                .Where(r => r.PatientId == patientId && r.Modality == BiometricModality.Face);

        private static readonly Expression<Func<PatientBiometricReference, FaceReferenceRow>> FaceRow = r =>
            new FaceReferenceRow(r.Id, r.Provider, r.SubjectReference, r.AlgorithmVersion, r.Status, r.CreatedAtUtc, r.RevokedAtUtc);

        public async Task<EncounterRow?> GetEncounterAsync(Guid encounterId, CancellationToken cancellationToken) =>
            (await ToRowsAsync(_clinical.ClinicalEncounters.Where(e => e.Id == encounterId), 1, cancellationToken)).SingleOrDefault();

        public Task<bool> HasActiveStaffAssignmentAsync(Guid encounterId, Guid staffId, CancellationToken cancellationToken) =>
            _clinical.EncounterStaffAssignments.AsNoTracking().AnyAsync(
                a => a.ClinicalEncounterId == encounterId && a.ClinicalStaffId == staffId && a.UnassignedAtUtc == null,
                cancellationToken);

        public async Task<IReadOnlyList<AssignedEmergencyRow>> ListAssignedPrehospitalEmergenciesAsync(
            Guid staffId,
            IReadOnlyCollection<ClinicalEncounterStatus> statuses,
            int limit,
            CancellationToken cancellationToken)
        {
            var encounters = await ToRowsAsync(
                _clinical.ClinicalEncounters.Where(e =>
                    e.Type == ClinicalEncounterType.Prehospital
                    && e.ClinicId == null
                    && statuses.Contains(e.Status)
                    && _clinical.EncounterStaffAssignments.Any(a =>
                        a.ClinicalEncounterId == e.Id && a.ClinicalStaffId == staffId && a.UnassignedAtUtc == null)),
                limit,
                cancellationToken);
            if (encounters.Count == 0)
            {
                return [];
            }

            // At most one active transfer per encounter (unique index).
            var encounterIds = encounters.Select(e => e.Id).ToList();
            var activeTransfers = await _clinical.ClinicalTransfers.AsNoTracking()
                .Where(t => encounterIds.Contains(t.ClinicalEncounterId)
                    && Domain.Entities.Transport.ClinicalTransfer.ActiveStatuses.Contains(t.Status))
                .Select(t => new { t.ClinicalEncounterId, t.Status })
                .ToDictionaryAsync(t => t.ClinicalEncounterId, t => t.Status, cancellationToken);

            return encounters
                .Select(e => new AssignedEmergencyRow(e, activeTransfers.TryGetValue(e.Id, out var status) ? status : null))
                .ToList();
        }

        public Task<IReadOnlyList<EncounterRow>> ListEncountersAsync(
            Guid clinicId,
            IReadOnlyCollection<ClinicalEncounterStatus> excludedStatuses,
            bool onlyUnidentified,
            int limit,
            CancellationToken cancellationToken)
        {
            var query = _clinical.ClinicalEncounters.Where(e => e.ClinicId == clinicId && !excludedStatuses.Contains(e.Status));
            if (onlyUnidentified)
            {
                query = query.Where(e => e.PatientId == null);
            }

            return ToRowsAsync(query, limit, cancellationToken);
        }

        public async Task<IReadOnlyList<TriageSummary>> ListTriagesAsync(Guid encounterId, CancellationToken cancellationToken) =>
            await _clinical.Triages.AsNoTracking()
                .Where(t => t.ClinicalEncounterId == encounterId)
                .OrderByDescending(t => t.PerformedAtUtc)
                .Select(t => new TriageSummary(
                    t.Id, t.ClinicalEncounterId, t.Priority, t.Reason, t.Observations, t.PerformedAtUtc, t.PerformedByStaffId))
                .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<VitalSignSummary>> ListVitalSignsAsync(Guid encounterId, CancellationToken cancellationToken) =>
            await _clinical.VitalSigns.AsNoTracking()
                .Where(v => v.ClinicalEncounterId == encounterId)
                .OrderByDescending(v => v.MeasuredAtUtc)
                .Select(v => new VitalSignSummary(
                    v.Id, v.ClinicalEncounterId, v.TriageId, v.Type, v.Value, v.Unit, v.MeasuredAtUtc, v.MeasuredByStaffId))
                .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<ClinicalNoteSummary>> ListNotesAsync(Guid encounterId, CancellationToken cancellationToken) =>
            await _clinical.ClinicalNotes.AsNoTracking()
                .Where(n => n.ClinicalEncounterId == encounterId)
                .Join(_clinical.ClinicalStaffMembers, n => n.AuthorStaffId, s => s.Id, (n, s) => new { Note = n, Author = s })
                .OrderByDescending(x => x.Note.OccurredAtUtc).ThenByDescending(x => x.Note.CreatedAtUtc)
                .Select(x => new ClinicalNoteSummary(
                    x.Note.Id, x.Note.ClinicalEncounterId, x.Note.Type, x.Note.Content, x.Note.OccurredAtUtc, x.Note.CreatedAtUtc,
                    x.Note.AuthorStaffId, x.Author.GivenNames + " " + x.Author.FamilyNames))
                .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<DiagnosisSummary>> ListDiagnosesAsync(Guid encounterId, CancellationToken cancellationToken) =>
            await _clinical.Diagnoses.AsNoTracking()
                .Where(d => d.ClinicalEncounterId == encounterId)
                .OrderByDescending(d => d.IsPrimary).ThenByDescending(d => d.RecordedAtUtc)
                .Select(d => new DiagnosisSummary(
                    d.Id, d.ClinicalEncounterId, d.Description, d.CodeSystem, d.Code, d.Type, d.IsPrimary, d.RecordedAtUtc,
                    d.ConfirmedAtUtc, d.RecordedByStaffId))
                .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<MedicationSummary>> ListMedicationsAsync(CancellationToken cancellationToken) =>
            await _clinical.Medications.AsNoTracking()
                .OrderBy(m => m.Name).ThenBy(m => m.Concentration)
                .Select(_medication)
                .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<MedicationSummary>> FindMedicationsAsync(
            IReadOnlyCollection<Guid> medicationIds, CancellationToken cancellationToken) =>
            await _clinical.Medications.AsNoTracking()
                .Where(m => medicationIds.Contains(m.Id))
                .Select(_medication)
                .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<PrescriptionSummary>> ListPrescriptionsAsync(Guid encounterId, CancellationToken cancellationToken) =>
            await _clinical.Prescriptions.AsNoTracking()
                .Where(p => p.ClinicalEncounterId == encounterId)
                .OrderByDescending(p => p.IssuedAtUtc ?? p.CreatedAtUtc).ThenByDescending(p => p.CreatedAtUtc)
                .Select(_prescription)
                .ToListAsync(cancellationToken);

        public Task<PrescriptionSummary?> GetPrescriptionAsync(Guid prescriptionId, CancellationToken cancellationToken) =>
            _clinical.Prescriptions.AsNoTracking()
                .Where(p => p.Id == prescriptionId)
                .Select(_prescription)
                .SingleOrDefaultAsync(cancellationToken);

        public async Task<IReadOnlyList<MedicationAdministrationSummary>> ListMedicationAdministrationsAsync(
            Guid encounterId, CancellationToken cancellationToken) =>
            await _clinical.MedicationAdministrations.AsNoTracking()
                .Where(a => a.ClinicalEncounterId == encounterId)
                .OrderByDescending(a => a.AdministeredAtUtc)
                .Select(a => new MedicationAdministrationSummary(
                    a.Id, a.ClinicalEncounterId, a.MedicationId, a.Medication.Name, a.PrescriptionId, a.PrescriptionDetailId, a.Dose,
                    a.DoseUnit, a.Route, a.AdministeredAtUtc, a.AdministeredByStaffId, a.Status, a.Notes))
                .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<ClinicalProcedureSummary>> ListProceduresAsync(Guid encounterId, CancellationToken cancellationToken) =>
            await _clinical.ClinicalProcedures.AsNoTracking()
                .Where(p => p.ClinicalEncounterId == encounterId)
                .OrderByDescending(p => p.PerformedAtUtc.HasValue).ThenByDescending(p => p.PerformedAtUtc).ThenBy(p => p.Id)
                .Select(p => new ClinicalProcedureSummary(
                    p.Id, p.ClinicalEncounterId, p.Name, p.CodeSystem, p.Code, p.Status, p.PerformedAtUtc, p.CompletedAtUtc, p.Outcome,
                    _clinical.ClinicalStaffMembers.Where(s => s.Id == p.PerformedByStaffId)
                        .Select(s => s.GivenNames + " " + s.FamilyNames).FirstOrDefault()))
                .ToListAsync(cancellationToken);

        public Task<IReadOnlyList<TransferRow>> ListEncounterTransfersAsync(Guid encounterId, int limit, CancellationToken cancellationToken) =>
            ToTransferRowsAsync(_clinical.ClinicalTransfers.Where(t => t.ClinicalEncounterId == encounterId), limit, cancellationToken);

        public async Task<ClinicStatus?> GetClinicStatusAsync(Guid clinicId, CancellationToken cancellationToken) =>
            await _clinical.Clinics.AsNoTracking()
                .Where(c => c.Id == clinicId)
                .Select(c => (ClinicStatus?)c.Status)
                .SingleOrDefaultAsync(cancellationToken);

        public Task<bool> HasActiveTransferAsync(Guid encounterId, CancellationToken cancellationToken) =>
            _clinical.ClinicalTransfers.AnyAsync(
                t => t.ClinicalEncounterId == encounterId && Domain.Entities.Transport.ClinicalTransfer.ActiveStatuses.Contains(t.Status),
                cancellationToken);

        public async Task<Guid?> FindActiveTransferIdAsync(Guid encounterId, CancellationToken cancellationToken) =>
            await _clinical.ClinicalTransfers.AsNoTracking()
                .Where(t => t.ClinicalEncounterId == encounterId && Domain.Entities.Transport.ClinicalTransfer.ActiveStatuses.Contains(t.Status))
                .Select(t => (Guid?)t.Id)
                .SingleOrDefaultAsync(cancellationToken);

        public async Task<IReadOnlyList<TransferDestinationRow>> ListTransferDestinationsAsync(CancellationToken cancellationToken) =>
            await _clinical.Clinics.AsNoTracking()
                .Where(c => c.Status == ClinicStatus.Active)
                .OrderBy(c => c.Name).ThenBy(c => c.Municipality).ThenBy(c => c.Id)
                .Select(c => new TransferDestinationRow(c.Id, c.Name, c.Type, c.Municipality, c.Province, c.Status))
                .ToListAsync(cancellationToken);

        public async Task<TransferDestinationRow?> GetTransferDestinationAsync(Guid clinicId, CancellationToken cancellationToken) =>
            await _clinical.Clinics.AsNoTracking()
                .Where(c => c.Id == clinicId)
                .Select(c => new TransferDestinationRow(c.Id, c.Name, c.Type, c.Municipality, c.Province, c.Status))
                .SingleOrDefaultAsync(cancellationToken);

        public async Task<TransferRow?> GetTransferAsync(Guid transferId, CancellationToken cancellationToken) =>
            (await ToTransferRowsAsync(_clinical.ClinicalTransfers.Where(t => t.Id == transferId), 1, cancellationToken)).SingleOrDefault();

        public Task<IReadOnlyList<TransferRow>> ListTransfersToAsync(
            IReadOnlyCollection<Guid> destinationClinicIds,
            IReadOnlyCollection<ClinicalTransferStatus> statuses,
            int limit,
            CancellationToken cancellationToken) =>
            ToTransferRowsAsync(
                _clinical.ClinicalTransfers.Where(t => destinationClinicIds.Contains(t.DestinationClinicId) && statuses.Contains(t.Status)),
                limit,
                cancellationToken);

        // Transfers with their clinics, ambulance and encounter first, then the patients in a second query.
        private async Task<IReadOnlyList<TransferRow>> ToTransferRowsAsync(
            IQueryable<Domain.Entities.Transport.ClinicalTransfer> query, int limit, CancellationToken cancellationToken)
        {
            var transfers = await query.AsNoTracking()
                .OrderByDescending(t => t.RequestedAtUtc)
                .Take(limit)
                .Select(t => new
                {
                    t.Id,
                    t.ClinicalEncounterId,
                    t.Status,
                    Origin = t.OriginClinic == null ? null : new ClinicSummary(t.OriginClinic.Id, t.OriginClinic.Name),
                    Destination = new ClinicSummary(t.DestinationClinic.Id, t.DestinationClinic.Name),
                    Ambulance = t.Ambulance == null ? null : new AmbulanceSummary(t.Ambulance.Id, t.Ambulance.Code, t.Ambulance.CallSign),
                    t.RequestedAtUtc,
                    t.SentAtUtc,
                    t.SentByStaffId,
                    t.ReceivedAtUtc,
                    t.ReceivedByStaffId,
                    t.CancelledAtUtc,
                    t.Reason,
                    t.Notes,
                    t.Encounter.PatientId,
                    t.Encounter.TemporaryPatientCode,
                    EncounterStatus = t.Encounter.Status,
                    LatestTriagePriority = t.Encounter.Triages
                        .OrderByDescending(x => x.PerformedAtUtc)
                        .Select(x => (TriagePriority?)x.Priority)
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            var patientIds = transfers.Where(t => t.PatientId.HasValue).Select(t => t.PatientId!.Value).Distinct().ToList();
            var patients = patientIds.Count == 0
                ? []
                : await _clinical.Patients.AsNoTracking()
                    .Where(p => patientIds.Contains(p.Id))
                    .Select(_patientRow)
                    .ToDictionaryAsync(p => p.Id, cancellationToken);

            return transfers
                .Select(t => new TransferRow(
                    t.Id, t.ClinicalEncounterId, t.Status, t.Origin, t.Destination, t.Ambulance, t.RequestedAtUtc, t.SentAtUtc,
                    t.SentByStaffId, t.ReceivedAtUtc, t.ReceivedByStaffId, t.CancelledAtUtc,
                    t.PatientId is { } id ? patients.GetValueOrDefault(id) : null,
                    t.TemporaryPatientCode?.Value, t.Reason, t.Notes, t.LatestTriagePriority, t.EncounterStatus))
                .ToList();
        }

        // Encounters first, then their patients in a second query (same patient projection as the searches).
        private async Task<IReadOnlyList<EncounterRow>> ToRowsAsync(
            IQueryable<Domain.Entities.Encounters.ClinicalEncounter> query, int limit, CancellationToken cancellationToken)
        {
            var encounters = await query.AsNoTracking()
                .OrderByDescending(e => e.StartedAtUtc)
                .Take(limit)
                .Select(e => new
                {
                    e.Id,
                    e.PatientId,
                    e.TemporaryPatientCode,
                    e.ClinicId,
                    e.Type,
                    e.Status,
                    e.Reason,
                    e.StartedAtUtc,
                    e.EndedAtUtc,
                    e.IdentifiedAtUtc,
                    LatestTriagePriority = e.Triages
                        .OrderByDescending(t => t.PerformedAtUtc)
                        .Select(t => (TriagePriority?)t.Priority)
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            var patientIds = encounters.Where(e => e.PatientId.HasValue).Select(e => e.PatientId!.Value).Distinct().ToList();
            var patients = patientIds.Count == 0
                ? []
                : await _clinical.Patients.AsNoTracking()
                    .Where(p => patientIds.Contains(p.Id))
                    .Select(_patientRow)
                    .ToDictionaryAsync(p => p.Id, cancellationToken);

            return encounters
                .Select(e => new EncounterRow(
                    e.Id,
                    e.PatientId,
                    e.PatientId is { } id ? patients.GetValueOrDefault(id) : null,
                    e.TemporaryPatientCode?.Value,
                    e.ClinicId,
                    e.Type,
                    e.Status,
                    e.Reason,
                    e.StartedAtUtc,
                    e.EndedAtUtc,
                    e.IdentifiedAtUtc,
                    e.LatestTriagePriority))
                .ToList();
        }
    }
}
