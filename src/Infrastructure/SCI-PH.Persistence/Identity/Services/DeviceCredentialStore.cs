using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Persistence.Contexts;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.Persistence.Identity.Services
{
    internal sealed class DeviceCredentialStore : IDeviceCredentialStore
    {
        private readonly IdentityDbContext _db;
        private readonly TimeProvider _time;

        public DeviceCredentialStore(IdentityDbContext db, TimeProvider time)
        {
            _db = db;
            _time = time;
        }

        public async Task<Guid?> AddAsync(NewDeviceCredential credential, CancellationToken cancellationToken)
        {
            if (await _db.DeviceCredentials.AnyAsync(d => d.DeviceCode == credential.DeviceCode, cancellationToken))
            {
                return null;
            }

            var device = DeviceCredential.Provision(credential, _time.GetUtcNow());
            _db.DeviceCredentials.Add(device);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return device.Id;
            }
            catch (DbUpdateException)
            {
                // A concurrent provisioning took the same code; the unique index decides.
                _db.Entry(device).State = EntityState.Detached;
                return null;
            }
        }

        public async Task<StoredDeviceCredential?> FindByCodeAsync(string deviceCode, CancellationToken cancellationToken) =>
            (await _db.DeviceCredentials.AsNoTracking().SingleOrDefaultAsync(d => d.DeviceCode == deviceCode, cancellationToken))?.ToStored();

        public async Task<IReadOnlyList<DeviceSummary>> ListAsync(CancellationToken cancellationToken) =>
            (await _db.DeviceCredentials.AsNoTracking().OrderBy(d => d.DeviceCode).ToListAsync(cancellationToken))
                .Select(d => d.ToSummary())
                .ToArray();

        public async Task<DeviceSummary?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            (await _db.DeviceCredentials.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken))?.ToSummary();

        public async Task<string?> ReplaceSecretAsync(Guid id, string secretHash, CancellationToken cancellationToken)
        {
            var device = await _db.DeviceCredentials.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
            if (device is not { IsActive: true })
            {
                return null;
            }

            device.ReplaceSecret(secretHash, _time.GetUtcNow());
            await _db.SaveChangesAsync(cancellationToken);
            return device.DeviceCode;
        }

        public async Task<bool> RevokeAsync(Guid id, CancellationToken cancellationToken)
        {
            var device = await _db.DeviceCredentials.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
            if (device is null)
            {
                return false;
            }

            if (device.IsActive)
            {
                device.Revoke(_time.GetUtcNow());
                await _db.SaveChangesAsync(cancellationToken);
            }

            return true;
        }

        public async Task RecordSeenAsync(Guid id, string? firmwareVersion, CancellationToken cancellationToken)
        {
            var device = await _db.DeviceCredentials.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
            if (device is { IsActive: true })
            {
                device.RecordSeen(_time.GetUtcNow(), firmwareVersion);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
