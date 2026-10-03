namespace SCI_PH.Application.Abstractions.Security
{
    // Persisted MFA challenges, so each challenge can produce at most one session.
    // Only the SHA-256 hash of the challenge identifier (the JWT jti) is stored.
    public interface IMfaChallengeStore
    {
        // Stores a new challenge and removes every previous challenge of the user (active or expired).
        Task AddAsync(Guid userId, string challengeHash, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken);

        // Atomically marks the challenge as consumed (compare-and-set on ConsumedAtUtc).
        // Returns its id, or null when it does not exist, belongs to another user, expired, was already
        // consumed or another request claimed it first.
        Task<Guid?> TryClaimAsync(Guid userId, string challengeHash, CancellationToken cancellationToken);

        // Returns a claimed challenge to the unconsumed state after a wrong code.
        Task ReleaseAsync(Guid challengeId, CancellationToken cancellationToken);
    }
}
