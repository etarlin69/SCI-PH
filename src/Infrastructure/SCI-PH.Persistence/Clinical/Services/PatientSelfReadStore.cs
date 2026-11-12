using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.Persistence.Clinical.Services
{
    // One patient's own record (ADR-015). Each query starts from the patientId resolved by PatientSelfService: child
    // rows are reached through their encounter's PatientId, so an encounter id of another patient matches nothing.
    internal sealed class PatientSelfReadStore : IPatientSelfReadStore
    {
        private static readonly Expression<Func<ClinicalEncounter, PatientSelfEncounter>> _encounter = e => new PatientSelfEncounter(
            e.Id, e.Type, PatientSelfVisibility.EncounterStatus(e.Status), e.StartedAtUtc, e.EndedAtUtc, e.Clinic != null ? e.Clinic.Name : null);

        private static readonly Expression<Func<Diagnosis, PatientSelfDiagnosis>> _diagnosis = d => new PatientSelfDiagnosis(
            d.Id, d.ClinicalEncounterId, d.Description, d.Type, d.RecordedAtUtc);

        private static readonly Expression<Func<ClinicalResult, PatientSelfResult>> _result = r => new PatientSelfResult(
            r.Id, r.ClinicalEncounterId, r.Type, r.Status, r.Name, r.ResultText, r.NumericValue, r.Unit,
            r.ReferenceRange, r.ResultedAtUtc);

        // Details have no entry order of their own: they are listed by medication name.
        private static readonly Expression<Func<Prescription, PatientSelfPrescription>> _prescription = p => new PatientSelfPrescription(
            p.Id,
            p.ClinicalEncounterId,
            p.Status,
            p.IssuedAtUtc,
            p.ValidUntilUtc,
            p.CancelledAtUtc,
            p.Details
                .OrderBy(d => d.Medication.Name)
                .Select(d => new PatientSelfPrescriptionItem(
                    d.Medication.Name, d.Medication.Presentation, d.Medication.Concentration, d.Dose, d.DoseUnit, d.Route,
                    d.Frequency, d.DurationDays, d.Instructions))
                .ToList());

        private readonly ClinicalDbContext _clinical;

        public PatientSelfReadStore(ClinicalDbContext clinical) => _clinical = clinical;

        // Primary active identifier of each type first, then the oldest active one.
        public Task<PatientSelfProfileRow?> GetProfileAsync(Guid patientId, CancellationToken cancellationToken) =>
            _clinical.Patients.AsNoTracking()
                .Where(p => p.Id == patientId)
                .Select(p => new PatientSelfProfileRow(
                    p.GivenNames,
                    p.FamilyNames,
                    p.DateOfBirth,
                    p.Sex,
                    p.Identifiers
                        .Where(i => i.Status == PatientIdentifierStatus.Active
                            && (i.Type == PatientIdentifierType.NationalId || i.Type == PatientIdentifierType.Passport))
                        .OrderByDescending(i => i.IsPrimary)
                        .ThenBy(i => i.CreatedAtUtc)
                        .Select(i => new PatientIdentifierRow(i.Type, i.NormalizedValue))
                        .FirstOrDefault(),
                    p.Identifiers
                        .Where(i => i.Status == PatientIdentifierStatus.Active && i.Type == PatientIdentifierType.Email)
                        .OrderByDescending(i => i.IsPrimary)
                        .ThenBy(i => i.CreatedAtUtc)
                        .Select(i => i.Value)
                        .FirstOrDefault(),
                    p.Identifiers
                        .Where(i => i.Status == PatientIdentifierStatus.Active && i.Type == PatientIdentifierType.Phone)
                        .OrderByDescending(i => i.IsPrimary)
                        .ThenBy(i => i.CreatedAtUtc)
                        .Select(i => i.Value)
                        .FirstOrDefault()))
                .SingleOrDefaultAsync(cancellationToken);

        public Task<int> CountEncountersAsync(Guid patientId, CancellationToken cancellationToken) =>
            Encounters(patientId).CountAsync(cancellationToken);

        public async Task<IReadOnlyList<PatientSelfEncounter>> ListEncountersAsync(
            Guid patientId, int limit, CancellationToken cancellationToken) =>
            await Encounters(patientId)
                .OrderByDescending(e => e.StartedAtUtc)
                .ThenBy(e => e.Id)
                .Take(limit)
                .Select(_encounter)
                .ToListAsync(cancellationToken);

        public Task<PatientSelfEncounter?> GetEncounterAsync(Guid patientId, Guid encounterId, CancellationToken cancellationToken) =>
            Encounters(patientId).Where(e => e.Id == encounterId).Select(_encounter).SingleOrDefaultAsync(cancellationToken);

        // Only the latest version of the medical history is the current one.
        public async Task<IReadOnlyList<PatientSelfAllergy>> ListAllergiesAsync(Guid patientId, CancellationToken cancellationToken)
        {
            var latestHistoryId = await _clinical.PatientMedicalHistories.AsNoTracking()
                .Where(h => h.PatientId == patientId)
                .OrderByDescending(h => h.Version)
                .Select(h => (Guid?)h.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (latestHistoryId is null)
            {
                return [];
            }

            return await _clinical.PatientAllergies.AsNoTracking()
                .Where(a => a.MedicalHistoryId == latestHistoryId)
                .OrderBy(a => a.Substance)
                .ThenBy(a => a.Id)
                .Select(a => new PatientSelfAllergy(a.Id, a.Substance, a.Severity, a.Status, a.Reaction, a.OnsetDate))
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<PatientSelfDiagnosis>> ListDiagnosesAsync(
            Guid patientId, Guid? encounterId, int limit, CancellationToken cancellationToken) =>
            await _clinical.Diagnoses.AsNoTracking()
                .Where(d => d.Encounter.PatientId == patientId
                    && (encounterId == null || d.ClinicalEncounterId == encounterId)
                    && PatientSelfVisibility.PublishedDiagnosisTypes.Contains(d.Type)
                    && PatientSelfVisibility.PublishedDiagnosisEncounterStatuses.Contains(d.Encounter.Status))
                .OrderByDescending(d => d.RecordedAtUtc)
                .ThenBy(d => d.Id)
                .Take(limit)
                .Select(_diagnosis)
                .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<PatientSelfPrescription>> ListPrescriptionsAsync(
            Guid patientId, Guid? encounterId, int limit, CancellationToken cancellationToken) =>
            await _clinical.Prescriptions.AsNoTracking()
                .Where(p => p.Encounter.PatientId == patientId
                    && (encounterId == null || p.ClinicalEncounterId == encounterId)
                    && p.Encounter.Status != PatientSelfVisibility.WithheldRecordsEncounterStatus
                    && p.IssuedAtUtc != null)
                .OrderByDescending(p => p.IssuedAtUtc)
                .ThenBy(p => p.Id)
                .Take(limit)
                .Select(_prescription)
                .ToListAsync(cancellationToken);

        public Task<int> CountResultsAsync(Guid patientId, CancellationToken cancellationToken) =>
            PublishedResults(patientId, null).CountAsync(cancellationToken);

        public async Task<IReadOnlyList<PatientSelfResult>> ListResultsAsync(
            Guid patientId, Guid? encounterId, int limit, CancellationToken cancellationToken) =>
            await PublishedResults(patientId, encounterId)
                .OrderByDescending(r => r.ResultedAtUtc)
                .ThenBy(r => r.Id)
                .Take(limit)
                .Select(_result)
                .ToListAsync(cancellationToken);

        private IQueryable<ClinicalEncounter> Encounters(Guid patientId) =>
            _clinical.ClinicalEncounters.AsNoTracking().Where(e => e.PatientId == patientId);

        private IQueryable<ClinicalResult> PublishedResults(Guid patientId, Guid? encounterId) =>
            _clinical.ClinicalResults.AsNoTracking()
                .Where(r => r.Encounter.PatientId == patientId
                    && (encounterId == null || r.ClinicalEncounterId == encounterId)
                    && r.Encounter.Status != PatientSelfVisibility.WithheldRecordsEncounterStatus
                    && PatientSelfVisibility.PublishedResultStatuses.Contains(r.Status));
    }
}
