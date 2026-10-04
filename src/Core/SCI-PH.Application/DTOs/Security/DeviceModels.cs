using System.ComponentModel.DataAnnotations;
using SCI_PH.Application.Common.Security;

namespace SCI_PH.Application.DTOs.Security
{
    public sealed record ProvisionDeviceRequest(
        [Required, RegularExpression("^[A-Za-z0-9][A-Za-z0-9-]{2,59}$")] string DeviceCode,
        [Required, MaxLength(120)] string Name,
        [Required, MinLength(1), MaxLength(8)] IReadOnlyList<string> Scopes);

    // Secret is the raw device key, returned only once when created or rotated.
    public sealed record DeviceSecret(Guid Id, string DeviceCode, string Secret);

    public sealed record DeviceSummary(
        Guid Id,
        string DeviceCode,
        string Name,
        IReadOnlyList<string> Scopes,
        DeviceCredentialStatus Status,
        string? FirmwareVersion,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? LastSeenAtUtc,
        DateTimeOffset? RotatedAtUtc,
        DateTimeOffset? RevokedAtUtc);

    // Authenticated device principal: no human roles, no clinical permissions.
    public sealed record DeviceIdentity(Guid Id, string DeviceCode, IReadOnlyList<string> Scopes);

    public sealed record DeviceHeartbeatRequest([MaxLength(32)] string? FirmwareVersion, long? UptimeSeconds);

    public sealed record NewDeviceCredential(
        string DeviceCode,
        string Name,
        IReadOnlyList<string> Scopes,
        string SecretHash,
        Guid? CreatedByUserId);

    public sealed record StoredDeviceCredential(
        Guid Id,
        string DeviceCode,
        IReadOnlyList<string> Scopes,
        string SecretHash,
        DeviceCredentialStatus Status,
        DateTimeOffset? LastSeenAtUtc);

    public enum DeviceOperationOutcome
    {
        Succeeded = 1,
        NotFound = 2,
        AlreadyRevoked = 3,
        DuplicateCode = 4,
        InvalidScopes = 5
    }
}
