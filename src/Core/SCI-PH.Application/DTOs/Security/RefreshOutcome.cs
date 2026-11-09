namespace SCI_PH.Application.DTOs.Security
{
    // What IRefreshTokenStore.RotateAsync did with a token the caller had just read as active.
    public enum RefreshTokenRotationResult
    {
        // The token was revoked as Rotated and its replacement stored.
        Rotated = 1,

        // Another request rotated the same token between this request's read (token active) and its update. Both
        // requests presented the token while it was still current, so this is a lost race, not a replay.
        LostConcurrencyRace = 2,

        // The token was revoked for any other reason in the meantime (logout, family revocation, deactivation...).
        Revoked = 3
    }

    public enum RefreshStatus
    {
        Succeeded = 1,

        // Retryable and harmless: no token is issued, nothing is revoked and the client must keep its cookie. A web
        // browser that retries sends the token the winning request stored in the shared cookie jar.
        LostConcurrencyRace = 2,

        // Invalid, expired, revoked or replayed token: the session is over (generic 401).
        Failed = 3
    }

    // Session is set only when Status is Succeeded.
    public sealed record RefreshOutcome(RefreshStatus Status, AuthenticationResult? Session)
    {
        public static RefreshOutcome Failed { get; } = new(RefreshStatus.Failed, null);

        public static RefreshOutcome LostConcurrencyRace { get; } = new(RefreshStatus.LostConcurrencyRace, null);

        public static RefreshOutcome Succeeded(AuthenticationResult session) => new(RefreshStatus.Succeeded, session);
    }
}
