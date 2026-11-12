using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Persistence.Clinical.Biometrics;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.Persistence.Clinical.Services
{
    // Enrollments share the scoped ClinicalDbContext with ClinicalWriteStore (one transaction with the reference, the audit
    // entry and the receipt), as FingerprintSessionStore does for identification sessions.
    internal sealed class FingerprintEnrollmentStore : IFingerprintEnrollmentStore
    {
        private readonly ClinicalDbContext _clinical;

        public FingerprintEnrollmentStore(ClinicalDbContext clinical) => _clinical = clinical;

        public async Task<FingerprintEnrollment?> FindAsync(Guid enrollmentId, CancellationToken cancellationToken) =>
            (await _clinical.FingerprintEnrollmentSessions.AsNoTracking()
                .SingleOrDefaultAsync(e => e.Id == enrollmentId, cancellationToken))
            ?.ToEnrollment();

        public async Task<FingerprintEnrollment?> FindPendingForReaderAsync(Guid deviceCredentialId, DateTimeOffset now, CancellationToken cancellationToken) =>
            (await _clinical.FingerprintEnrollmentSessions.AsNoTracking()
                .SingleOrDefaultAsync(e => e.DeviceCredentialId == deviceCredentialId
                    && e.Status == FingerprintEnrollmentStatus.Pending
                    && e.ExpiresAtUtc > now, cancellationToken))
            ?.ToEnrollment();

        // One UPDATE with its own implicit transaction; a concurrent writer that read the enrollment before loses on Status.
        public Task ExpireDueAsync(FingerprintEnrollmentScope scope, DateTimeOffset now, CancellationToken cancellationToken) =>
            _clinical.FingerprintEnrollmentSessions
                .Where(e => e.Status == FingerprintEnrollmentStatus.Pending
                    && e.ExpiresAtUtc <= now
                    && ((scope.EnrollmentId != null && e.Id == scope.EnrollmentId)
                        || (scope.PatientId != null && e.PatientId == scope.PatientId)
                        || (scope.DeviceCredentialId != null && e.DeviceCredentialId == scope.DeviceCredentialId)))
                .ExecuteUpdateAsync(set => set.SetProperty(e => e.Status, FingerprintEnrollmentStatus.Expired), cancellationToken);

        public void Add(FingerprintEnrollment enrollment) =>
            _clinical.FingerprintEnrollmentSessions.Add(FingerprintEnrollmentSessionEntry.From(enrollment));

        // UPDATE ... WHERE "Status" = expectedStatus.
        public void Update(FingerprintEnrollment enrollment, FingerprintEnrollmentStatus expectedStatus)
        {
            var entry = _clinical.Attach(FingerprintEnrollmentSessionEntry.From(enrollment));
            foreach (var property in entry.Properties.Where(p => !p.Metadata.IsPrimaryKey()))
            {
                property.IsModified = true;
            }

            entry.Property(e => e.Status).OriginalValue = expectedStatus;
        }
    }
}
