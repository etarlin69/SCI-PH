using System.Text;
using Microsoft.Extensions.Options;

namespace SCI_PH.Infrastructure.Security
{
    // Fails startup on unsafe settings. Messages never include the signing key.
    public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
    {
        // HMAC-SHA256 needs at least a 256-bit key.
        public const int MinimumSigningKeyBytes = 32;
        public const int MaxAccessTokenLifetimeMinutes = 60;
        public const int MaxRefreshTokenLifetimeDays = 90;
        public const int MaxClockSkewSeconds = 300;
        public const int MaxMfaChallengeLifetimeMinutes = 10;

        public ValidateOptionsResult Validate(string? name, JwtOptions options)
        {
            var failures = new List<string>();

            if (string.IsNullOrWhiteSpace(options.Issuer))
            {
                failures.Add("Jwt:Issuer is required.");
            }

            if (string.IsNullOrWhiteSpace(options.Audience))
            {
                failures.Add("Jwt:Audience is required.");
            }

            if (Encoding.UTF8.GetByteCount(options.SigningKey ?? string.Empty) < MinimumSigningKeyBytes)
            {
                failures.Add($"Jwt:SigningKey is missing or shorter than {MinimumSigningKeyBytes} bytes. Configure it with User Secrets or Jwt__SigningKey.");
            }

            if (options.AccessTokenLifetimeMinutes is < 1 or > MaxAccessTokenLifetimeMinutes)
            {
                failures.Add($"Jwt:AccessTokenLifetimeMinutes must be between 1 and {MaxAccessTokenLifetimeMinutes}.");
            }

            if (options.RefreshTokenLifetimeDays is < 1 or > MaxRefreshTokenLifetimeDays)
            {
                failures.Add($"Jwt:RefreshTokenLifetimeDays must be between 1 and {MaxRefreshTokenLifetimeDays}.");
            }

            if (options.ClockSkewSeconds is < 0 or > MaxClockSkewSeconds)
            {
                failures.Add($"Jwt:ClockSkewSeconds must be between 0 and {MaxClockSkewSeconds}.");
            }

            if (options.MfaChallengeLifetimeMinutes is < 1 or > MaxMfaChallengeLifetimeMinutes)
            {
                failures.Add($"Jwt:MfaChallengeLifetimeMinutes must be between 1 and {MaxMfaChallengeLifetimeMinutes}.");
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }
    }
}
