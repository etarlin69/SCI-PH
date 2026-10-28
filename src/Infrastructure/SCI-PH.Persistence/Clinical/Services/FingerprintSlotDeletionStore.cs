using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Persistence.Clinical.Biometrics;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.Persistence.Clinical.Services
{
    // Deletion jobs share the scoped ClinicalDbContext with ClinicalWriteStore (one transaction with the revocation and the
    // audit entries), as FingerprintEnrollmentStore does for enrollments.
    internal sealed class FingerprintSlotDeletionStore : IFingerprintSlotDeletionStore
    {
        private readonly ClinicalDbContext _clinical;

        public FingerprintSlotDeletionStore(ClinicalDbContext clinical) => _clinical = clinical;

        public async Task<FingerprintSlotDeletionTarget?> FindAsync(Guid deletionId, CancellationToken cancellationToken) =>
            (await Targets().SingleOrDefaultAsync(t => t.Job.Id == deletionId, cancellationToken))?.ToTarget();

        public async Task<FingerprintSlotDeletionTarget?> FindOldestPendingForReaderAsync(Guid deviceCredentialId, CancellationToken cancellationToken) =>
            (await Targets()
                .Where(t => t.Job.DeviceCredentialId == deviceCredentialId && t.Job.Status == FingerprintSlotDeletionStatus.Pending)
                .OrderBy(t => t.Job.CreatedAtUtc)
                .ThenBy(t => t.Job.Id)
                .FirstOrDefaultAsync(cancellationToken))
            ?.ToTarget();

        public async Task<IReadOnlyDictionary<Guid, FingerprintSlotDeletion>> ListByReferencesAsync(
            IReadOnlyCollection<Guid> biometricReferenceIds, CancellationToken cancellationToken) =>
            (await _clinical.FingerprintSlotDeletionJobs.AsNoTracking()
                .Where(j => biometricReferenceIds.Contains(j.BiometricReferenceId))
                .ToListAsync(cancellationToken))
            .ToDictionary(j => j.BiometricReferenceId, j => j.ToDeletion());

        public Task<bool> HasPendingForSubjectAsync(Guid deviceCredentialId, string subjectReference, CancellationToken cancellationToken) =>
            Targets().AnyAsync(t => t.Job.DeviceCredentialId == deviceCredentialId
                && t.Job.Status == FingerprintSlotDeletionStatus.Pending
                && t.SubjectReference == subjectReference, cancellationToken);

        public void Add(FingerprintSlotDeletion deletion) =>
            _clinical.FingerprintSlotDeletionJobs.Add(FingerprintSlotDeletionJobEntry.From(deletion));

        // UPDATE ... WHERE "Status" = expectedStatus AND "LastAttemptId" IS NOT DISTINCT FROM expectedLastAttemptId.
        public void Update(FingerprintSlotDeletion deletion, FingerprintSlotDeletionStatus expectedStatus, Guid? expectedLastAttemptId)
        {
            var entry = _clinical.Attach(FingerprintSlotDeletionJobEntry.From(deletion));
            foreach (var property in entry.Properties.Where(p => !p.Metadata.IsPrimaryKey()))
            {
                property.IsModified = true;
            }

            entry.Property(e => e.Status).OriginalValue = expectedStatus;
            entry.Property(e => e.LastAttemptId).OriginalValue = expectedLastAttemptId;
        }

        private IQueryable<TargetRow> Targets() =>
            from job in _clinical.FingerprintSlotDeletionJobs.AsNoTracking()
            join reference in _clinical.PatientBiometricReferences.AsNoTracking() on job.BiometricReferenceId equals reference.Id
            select new TargetRow { Job = job, PatientId = reference.PatientId, SubjectReference = reference.SubjectReference };

        // A member-initialized projection, so the queries above can still filter and sort on it.
        private sealed class TargetRow
        {
            public required FingerprintSlotDeletionJobEntry Job { get; init; }
            public required Guid PatientId { get; init; }
            public required string SubjectReference { get; init; }

            public FingerprintSlotDeletionTarget ToTarget() => new(Job.ToDeletion(), PatientId, SubjectReference);
        }
    }
}
