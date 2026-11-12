using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Persistence.Clinical.Biometrics;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.Persistence.Clinical.Services
{
    // Face template deletion jobs share the scoped ClinicalDbContext with ClinicalWriteStore (one transaction with the
    // revocation and the audit entries), as the fingerprint slot deletion jobs do.
    internal sealed class FaceTemplateDeletionStore : IFaceTemplateDeletionStore
    {
        private readonly ClinicalDbContext _clinical;

        public FaceTemplateDeletionStore(ClinicalDbContext clinical) => _clinical = clinical;

        public async Task<FaceTemplateDeletionTarget?> FindAsync(Guid deletionId, CancellationToken cancellationToken) =>
            (await Targets().SingleOrDefaultAsync(t => t.Job.Id == deletionId, cancellationToken))?.ToTarget();

        public async Task<IReadOnlyList<FaceTemplateDeletionTarget>> ListPendingAsync(int limit, CancellationToken cancellationToken) =>
            (await Targets()
                .Where(t => t.Job.Status == FaceTemplateDeletionStatus.Pending)
                .OrderBy(t => t.Job.CreatedAtUtc)
                .ThenBy(t => t.Job.Id)
                .Take(limit)
                .ToListAsync(cancellationToken))
            .Select(t => t.ToTarget())
            .ToList();

        public async Task<IReadOnlyList<RetainedFaceTemplate>> ListRetainedAsync(string provider, CancellationToken cancellationToken) =>
            (await (
                from reference in _clinical.PatientBiometricReferences.AsNoTracking()
                where reference.Modality == BiometricModality.Face && reference.Provider == provider
                join job in _clinical.FaceTemplateDeletionJobs.AsNoTracking() on reference.Id equals job.BiometricReferenceId into jobs
                from job in jobs.DefaultIfEmpty()
                where reference.Status == BiometricReferenceStatus.Active || job == null || job.Status == FaceTemplateDeletionStatus.Pending
                select new { reference.SubjectReference, Active = reference.Status == BiometricReferenceStatus.Active })
                .ToListAsync(cancellationToken))
            .Select(r => new RetainedFaceTemplate(r.SubjectReference, r.Active))
            .ToList();

        public void Add(FaceTemplateDeletion deletion) =>
            _clinical.FaceTemplateDeletionJobs.Add(FaceTemplateDeletionJobEntry.From(deletion));

        // UPDATE ... WHERE "Status" = expectedStatus AND "Attempts" = expectedAttempts.
        public void Update(FaceTemplateDeletion deletion, FaceTemplateDeletionStatus expectedStatus, int expectedAttempts)
        {
            // The revocation that added the job may have used this same context (the immediate cleanup attempt).
            if (_clinical.FaceTemplateDeletionJobs.Local.FirstOrDefault(e => e.Id == deletion.Id) is { } tracked)
            {
                _clinical.Entry(tracked).State = EntityState.Detached;
            }

            var entry = _clinical.Attach(FaceTemplateDeletionJobEntry.From(deletion));
            foreach (var property in entry.Properties.Where(p => !p.Metadata.IsPrimaryKey()))
            {
                property.IsModified = true;
            }

            entry.Property(e => e.Status).OriginalValue = expectedStatus;
            entry.Property(e => e.Attempts).OriginalValue = expectedAttempts;
        }

        private IQueryable<TargetRow> Targets() =>
            from job in _clinical.FaceTemplateDeletionJobs.AsNoTracking()
            join reference in _clinical.PatientBiometricReferences.AsNoTracking() on job.BiometricReferenceId equals reference.Id
            select new TargetRow
            {
                Job = job,
                PatientId = reference.PatientId,
                Modality = reference.Modality,
                Provider = reference.Provider,
                SubjectReference = reference.SubjectReference
            };

        // A member-initialized projection, so the queries above can still filter and sort on it.
        private sealed class TargetRow
        {
            public required FaceTemplateDeletionJobEntry Job { get; init; }
            public required Guid PatientId { get; init; }
            public required BiometricModality Modality { get; init; }
            public required string Provider { get; init; }
            public required string SubjectReference { get; init; }

            public FaceTemplateDeletionTarget ToTarget() => new(Job.ToDeletion(), PatientId, Modality, Provider, SubjectReference);
        }
    }
}
