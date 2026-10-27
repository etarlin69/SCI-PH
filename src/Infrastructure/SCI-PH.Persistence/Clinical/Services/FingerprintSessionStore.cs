using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Persistence.Clinical.Biometrics;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.Persistence.Clinical.Services
{
    // Sessions share the scoped ClinicalDbContext with ClinicalWriteStore: Add and Update only track the change and
    // ClinicalWriteStore.SaveChangesAsync commits it with the clinical rows (one transaction), translating a lost race on
    // the Status token or on the unique indexes into a ResourceConflictException.
    internal sealed class FingerprintSessionStore : IFingerprintSessionStore
    {
        private readonly ClinicalDbContext _clinical;

        public FingerprintSessionStore(ClinicalDbContext clinical) => _clinical = clinical;

        public async Task<FingerprintSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken) =>
            (await _clinical.FingerprintIdentificationSessions.AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken))
            ?.ToSession();

        public async Task<FingerprintSession?> FindPendingForReaderAsync(Guid deviceCredentialId, DateTimeOffset now, CancellationToken cancellationToken) =>
            (await _clinical.FingerprintIdentificationSessions.AsNoTracking()
                .SingleOrDefaultAsync(s => s.DeviceCredentialId == deviceCredentialId
                    && s.Status == FingerprintSessionStatus.Pending
                    && s.ExpiresAtUtc > now, cancellationToken))
            ?.ToSession();

        // One UPDATE with its own implicit transaction. A concurrent writer that read the session before keeps its original
        // Status in the WHERE of its own update, so it matches no row once this commits (409 for it).
        public Task ExpireDueAsync(FingerprintSessionScope scope, DateTimeOffset now, CancellationToken cancellationToken) =>
            _clinical.FingerprintIdentificationSessions
                .Where(s => (s.Status == FingerprintSessionStatus.Pending || s.Status == FingerprintSessionStatus.Matched)
                    && s.ExpiresAtUtc <= now
                    && ((scope.SessionId != null && s.Id == scope.SessionId)
                        || (scope.ClinicalEncounterId != null && s.ClinicalEncounterId == scope.ClinicalEncounterId)
                        || (scope.DeviceCredentialId != null && s.DeviceCredentialId == scope.DeviceCredentialId)))
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.Status, FingerprintSessionStatus.Expired), cancellationToken);

        public void Add(FingerprintSession session) =>
            _clinical.FingerprintIdentificationSessions.Add(FingerprintIdentificationSessionEntry.From(session));

        // The new state, compared on commit with the status it was read in: UPDATE ... WHERE "Status" = expectedStatus.
        public void Update(FingerprintSession session, FingerprintSessionStatus expectedStatus)
        {
            var entry = _clinical.Attach(FingerprintIdentificationSessionEntry.From(session));
            foreach (var property in entry.Properties.Where(p => !p.Metadata.IsPrimaryKey()))
            {
                property.IsModified = true;
            }

            entry.Property(s => s.Status).OriginalValue = expectedStatus;
        }
    }
}
