using SCI_PH.Application.Common.Security;

namespace SCI_PH.Application.DTOs.Security
{
    public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);

    // Value is the raw secret for the client; only Hash is persisted.
    public sealed record GeneratedRefreshToken(string Value, string Hash, DateTimeOffset ExpiresAtUtc);

    public sealed record NewRefreshToken(
        Guid UserId,
        Guid FamilyId,
        string TokenHash,
        DateTimeOffset ExpiresAtUtc,
        string? CreatedByIp);

    public sealed record StoredRefreshToken(
        Guid Id,
        Guid UserId,
        Guid FamilyId,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset? RevokedAtUtc,
        RefreshTokenRevocationReason? RevocationReason)
    {
        public bool IsRevoked => RevokedAtUtc.HasValue;
    }
}
