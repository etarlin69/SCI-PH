using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Persistence.Identity.Models
{
    // Stores only the SHA-256 hash of a refresh token; the raw value is returned once to the client.
    // Tokens of one login share FamilyId so a reused (already rotated) token revokes the whole session.
    public sealed class RefreshToken
    {
        private RefreshToken() { }

        public Guid Id { get; private set; }
        public Guid UserId { get; private set; }
        public Guid FamilyId { get; private set; }
        public string TokenHash { get; private set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; private set; }
        public DateTimeOffset ExpiresAtUtc { get; private set; }
        public string? CreatedByIp { get; private set; }
        public DateTimeOffset? RevokedAtUtc { get; private set; }
        public string? RevokedByIp { get; private set; }
        public RefreshTokenRevocationReason? RevocationReason { get; private set; }
        public Guid? ReplacedByTokenId { get; private set; }

        public static RefreshToken Issue(NewRefreshToken token, DateTimeOffset createdAtUtc) =>
            new()
            {
                Id = Guid.NewGuid(),
                UserId = token.UserId,
                FamilyId = token.FamilyId,
                TokenHash = token.TokenHash,
                CreatedAtUtc = createdAtUtc.ToUniversalTime(),
                ExpiresAtUtc = token.ExpiresAtUtc.ToUniversalTime(),
                CreatedByIp = SecurityFieldLengths.Limit(token.CreatedByIp, SecurityFieldLengths.IpAddress)
            };

        public void Revoke(DateTimeOffset revokedAtUtc, RefreshTokenRevocationReason reason, string? ipAddress, Guid? replacedByTokenId = null)
        {
            if (RevokedAtUtc.HasValue)
            {
                throw new InvalidOperationException("The refresh token is already revoked.");
            }

            RevokedAtUtc = revokedAtUtc.ToUniversalTime();
            RevocationReason = reason;
            RevokedByIp = SecurityFieldLengths.Limit(ipAddress, SecurityFieldLengths.IpAddress);
            ReplacedByTokenId = replacedByTokenId;
        }

        public StoredRefreshToken ToStored() =>
            new(Id, UserId, FamilyId, ExpiresAtUtc, RevokedAtUtc, RevocationReason);
    }
}
