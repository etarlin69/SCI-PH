using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.Persistence.Clinical.Services
{
    internal sealed class ClinicalReadStore : IClinicalReadStore
    {
        // Primary active identifier first, then the oldest active one.
        private static readonly Expression<Func<Patient, PatientRow>> _patientRow = p => new PatientRow(
            p.Id,
            p.GivenNames,
            p.FamilyNames,
            p.DateOfBirth,
            p.Sex,
            p.IdentityStatus,
            p.Identifiers
                .Where(i => i.Status == PatientIdentifierStatus.Active)
                .OrderByDescending(i => i.IsPrimary)
                .ThenBy(i => i.CreatedAtUtc)
                .Select(i => new PatientIdentifierRow(i.Type, i.NormalizedValue))
                .FirstOrDefault());

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

        public async Task<EncounterRow?> GetEncounterAsync(Guid encounterId, CancellationToken cancellationToken) =>
            (await ToRowsAsync(_clinical.ClinicalEncounters.Where(e => e.Id == encounterId), 1, cancellationToken)).SingleOrDefault();

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
