namespace SCI_PH.Infrastructure.Security
{
    // Non-secret values live in appsettings. SigningKey comes only from User Secrets (Jwt:SigningKey),
    // an environment variable (Jwt__SigningKey) or a vault; it is never committed or logged.
    public sealed class JwtOptions
    {
        public const string SectionName = "Jwt";

        public string Issuer { get; init; } = string.Empty;
        public string Audience { get; init; } = string.Empty;
        public string SigningKey { get; init; } = string.Empty;
        public int AccessTokenLifetimeMinutes { get; init; } = 15;
        public int RefreshTokenLifetimeDays { get; init; } = 7;
        public int ClockSkewSeconds { get; init; } = 30;
        public int MfaChallengeLifetimeMinutes { get; init; } = 5;
    }
}
