using System.Globalization;
using System.Security.Cryptography;

namespace SCI_PH.IntegrationTests.Security.Support
{
    internal static class TestSettings
    {
        // Random per test run: no signing key or password is stored in the repository.
        public static readonly string SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        public const string Issuer = "sci-ph-tests";
        public const string Audience = "sci-ph-tests-audience";
        public const int AccessTokenLifetimeMinutes = 15;
        public const int RefreshTokenLifetimeDays = 7;
        public const string FrontendOrigin = "https://frontend.sci-ph.test";
        public const string ActivationUrl = "https://frontend.sci-ph.test/activate-account";
        public const string ConfirmEmailUrl = "https://frontend.sci-ph.test/confirm-email";
        public const string ResetPasswordUrl = "https://frontend.sci-ph.test/reset-password";

        public static string NewPassword() => $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";

        public static Dictionary<string, string?> Create(IDictionary<string, string?>? overrides = null)
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:SciPhDatabase"] = "Host=localhost;Database=sciph_unused_by_tests;Username=unused",
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:AccessTokenLifetimeMinutes"] = AccessTokenLifetimeMinutes.ToString(CultureInfo.InvariantCulture),
                ["Jwt:RefreshTokenLifetimeDays"] = RefreshTokenLifetimeDays.ToString(CultureInfo.InvariantCulture),
                ["IdentityBootstrap:Enabled"] = "false",
                ["Email:Mode"] = "Disabled",
                ["AccountEmail:ActivationUrl"] = ActivationUrl,
                ["AccountEmail:ConfirmEmailUrl"] = ConfirmEmailUrl,
                ["AccountEmail:ResetPasswordUrl"] = ResetPasswordUrl,
                ["Security:AllowedOrigins:0"] = FrontendOrigin,
                // High limits so functional tests never hit them; the rate-limit test lowers them explicitly.
                ["RateLimiting:AuthenticationPermitLimit"] = "10000",
                ["RateLimiting:AccountRecoveryPermitLimit"] = "10000",
                ["RateLimiting:DevicePermitLimit"] = "10000",
                ["RateLimiting:PatientSelfServicePermitLimit"] = "10000"
            };

            foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
            {
                settings[key] = value;
            }

            return settings;
        }
    }
}
