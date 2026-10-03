using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SCI_PH.Infrastructure.Web
{
    // Browser origins allowed to call the Web BFF with credentials (the refresh cookie).
    public sealed class FrontendSecurityOptions
    {
        public const string SectionName = "Security";

        public string[] AllowedOrigins { get; init; } = [];
    }

    public static class FrontendCors
    {
        public const string PolicyName = "Frontend";
        // Custom header required by cookie endpoints; a cross-site form or image cannot add it (see ADR-006).
        public const string CsrfHeaderName = "X-SCIPH-CSRF";
    }

    // Exact origins only: never "*" (it cannot be combined with credentials) and HTTPS outside Development.
    public sealed class FrontendSecurityOptionsValidator : IValidateOptions<FrontendSecurityOptions>
    {
        private readonly IHostEnvironment _environment;

        public FrontendSecurityOptionsValidator(IHostEnvironment environment) => _environment = environment;

        public ValidateOptionsResult Validate(string? name, FrontendSecurityOptions options)
        {
            var failures = new List<string>();
            if (_environment.IsProduction() && options.AllowedOrigins.Length == 0)
            {
                failures.Add("Security:AllowedOrigins must list the web frontend origins in Production.");
            }

            foreach (var origin in options.AllowedOrigins)
            {
                var valid = Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                    && uri.GetLeftPart(UriPartial.Authority).Equals(origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                    && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && !_environment.IsProduction()));
                if (!valid)
                {
                    failures.Add($"Security:AllowedOrigins contains an invalid origin: '{origin}'. Use scheme://host[:port], HTTPS in Production.");
                }
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }
    }
}
