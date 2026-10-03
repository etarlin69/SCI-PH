using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Persistence.Identity.Models
{
    // Credential of an ESP32 device. Not an account: no relation with ApplicationUser, roles or clinical data.
    // Stores only the SHA-256 of the device key.
    public sealed class DeviceCredential
    {
        public const char ScopeSeparator = ' ';

        private DeviceCredential() { }

        public Guid Id { get; private set; }
        public string DeviceCode { get; private set; } = string.Empty;
        public string Name { get; private set; } = string.Empty;
        public string Scopes { get; private set; } = string.Empty;
        public string SecretHash { get; private set; } = string.Empty;
        public DeviceCredentialStatus Status { get; private set; }
        public string? FirmwareVersion { get; private set; }
        public DateTimeOffset CreatedAtUtc { get; private set; }
        public Guid? CreatedByUserId { get; private set; }
        public DateTimeOffset? LastSeenAtUtc { get; private set; }
        public DateTimeOffset? RotatedAtUtc { get; private set; }
        public DateTimeOffset? RevokedAtUtc { get; private set; }

        public bool IsActive => Status == DeviceCredentialStatus.Active;

        public IReadOnlyList<string> ScopeList => Scopes.Split(ScopeSeparator, StringSplitOptions.RemoveEmptyEntries);

        public static DeviceCredential Provision(NewDeviceCredential credential, DateTimeOffset createdAtUtc) =>
            new()
            {
                Id = Guid.NewGuid(),
                DeviceCode = credential.DeviceCode,
                Name = credential.Name,
                Scopes = string.Join(ScopeSeparator, credential.Scopes),
                SecretHash = credential.SecretHash,
                Status = DeviceCredentialStatus.Active,
                CreatedAtUtc = createdAtUtc.ToUniversalTime(),
                CreatedByUserId = credential.CreatedByUserId
            };

        public void ReplaceSecret(string secretHash, DateTimeOffset rotatedAtUtc)
        {
            SecretHash = secretHash;
            RotatedAtUtc = rotatedAtUtc.ToUniversalTime();
        }

        public void Revoke(DateTimeOffset revokedAtUtc)
        {
            Status = DeviceCredentialStatus.Revoked;
            RevokedAtUtc = revokedAtUtc.ToUniversalTime();
        }

        public void RecordSeen(DateTimeOffset seenAtUtc, string? firmwareVersion)
        {
            LastSeenAtUtc = seenAtUtc.ToUniversalTime();
            FirmwareVersion = string.IsNullOrWhiteSpace(firmwareVersion)
                ? FirmwareVersion
                : SecurityFieldLengths.Limit(firmwareVersion.Trim(), SecurityFieldLengths.FirmwareVersion);
        }

        public StoredDeviceCredential ToStored() => new(Id, DeviceCode, ScopeList, SecretHash, Status, LastSeenAtUtc);

        public DeviceSummary ToSummary() =>
            new(Id, DeviceCode, Name, ScopeList, Status, FirmwareVersion, CreatedAtUtc, LastSeenAtUtc, RotatedAtUtc, RevokedAtUtc);
    }
}
