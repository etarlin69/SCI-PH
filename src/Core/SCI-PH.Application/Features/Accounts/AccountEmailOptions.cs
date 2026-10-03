using Microsoft.Extensions.Options;

namespace SCI_PH.Application.Features.Accounts
{
    // Frontend pages that receive one-time tokens by email. Tokens travel in the query string of these URLs.
    public sealed class AccountEmailOptions
    {
        public const string SectionName = "AccountEmail";

        public string ActivationUrl { get; init; } = string.Empty;
        public string ConfirmEmailUrl { get; init; } = string.Empty;
        public string ResetPasswordUrl { get; init; } = string.Empty;
        public int InvitationLifetimeHours { get; init; } = 24;
    }

    public sealed class AccountEmailOptionsValidator : IValidateOptions<AccountEmailOptions>
    {
        public const int MaxInvitationLifetimeHours = 168;

        public ValidateOptionsResult Validate(string? name, AccountEmailOptions options)
        {
            var failures = new List<string>();
            CheckUrl(options.ActivationUrl, nameof(options.ActivationUrl), failures);
            CheckUrl(options.ConfirmEmailUrl, nameof(options.ConfirmEmailUrl), failures);
            CheckUrl(options.ResetPasswordUrl, nameof(options.ResetPasswordUrl), failures);

            if (options.InvitationLifetimeHours is < 1 or > MaxInvitationLifetimeHours)
            {
                failures.Add($"AccountEmail:InvitationLifetimeHours must be between 1 and {MaxInvitationLifetimeHours}.");
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }

        // HTTPS is required except for local development hosts.
        private static void CheckUrl(string value, string name, List<string> failures)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
                || !string.IsNullOrEmpty(uri.Query))
            {
                failures.Add($"AccountEmail:{name} must be an absolute HTTPS URL without query string (HTTP is allowed only for localhost).");
            }
        }
    }
}
