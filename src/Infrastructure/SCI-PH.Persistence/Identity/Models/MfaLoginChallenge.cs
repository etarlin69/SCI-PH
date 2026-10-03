namespace SCI_PH.Persistence.Identity.Models
{
    // Server-side record of an MFA login challenge so it can be used only once. Stores the SHA-256 hash of the
    // challenge identifier (the JWT jti), never the token, codes or authenticator key.
    public sealed class MfaLoginChallenge
    {
        private MfaLoginChallenge() { }

        public Guid Id { get; private set; }
        public Guid UserId { get; private set; }
        public string ChallengeHash { get; private set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; private set; }
        public DateTimeOffset ExpiresAtUtc { get; private set; }
        // Set when a request claims the challenge; cleared again only if that request presented a wrong code.
        public DateTimeOffset? ConsumedAtUtc { get; private set; }

        public static MfaLoginChallenge Issue(Guid userId, string challengeHash, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc) =>
            new()
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ChallengeHash = challengeHash,
                CreatedAtUtc = createdAtUtc.ToUniversalTime(),
                ExpiresAtUtc = expiresAtUtc.ToUniversalTime()
            };

        public void Consume(DateTimeOffset consumedAtUtc) => ConsumedAtUtc = consumedAtUtc.ToUniversalTime();

        public void Release() => ConsumedAtUtc = null;
    }
}
