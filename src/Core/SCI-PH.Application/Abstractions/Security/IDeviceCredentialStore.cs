using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Abstractions.Security
{
    public interface IDeviceCredentialStore
    {
        // Returns null when the device code is already registered.
        Task<Guid?> AddAsync(NewDeviceCredential credential, CancellationToken cancellationToken);

        Task<StoredDeviceCredential?> FindByCodeAsync(string deviceCode, CancellationToken cancellationToken);

        Task<IReadOnlyList<DeviceSummary>> ListAsync(CancellationToken cancellationToken);

        Task<DeviceSummary?> FindAsync(Guid id, CancellationToken cancellationToken);

        // Replaces the secret hash of an active device. Returns the device code, or null if not found or revoked.
        Task<string?> ReplaceSecretAsync(Guid id, string secretHash, CancellationToken cancellationToken);

        Task<bool> RevokeAsync(Guid id, CancellationToken cancellationToken);

        Task RecordSeenAsync(Guid id, string? firmwareVersion, CancellationToken cancellationToken);
    }
}
