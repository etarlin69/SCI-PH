using System.Net.Mail;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SCI_PH.Infrastructure.Email
{
    public enum EmailDeliveryMode
    {
        // Messages are dropped (only the subject is logged). Not allowed in Production.
        Disabled = 1,
        Smtp = 2,
        // Writes .eml files to a local folder outside the repository. Development and Testing only.
        PickupDirectory = 3
    }

    public sealed class EmailDeliveryOptions
    {
        public const string SectionName = "Email";

        public EmailDeliveryMode Mode { get; init; } = EmailDeliveryMode.Disabled;
        public string? PickupDirectory { get; init; }
    }

    // Password comes only from User Secrets, environment variables (Smtp__Password) or a vault.
    public sealed class SmtpOptions
    {
        public const string SectionName = "Smtp";

        public string Host { get; init; } = string.Empty;
        public int Port { get; init; } = 587;
        // true: TLS from the first byte (usually port 465). false: STARTTLS is required (usually 587).
        public bool UseSsl { get; init; }
        public string? UserName { get; init; }
        public string? Password { get; init; }
        public string FromAddress { get; init; } = string.Empty;
        public string FromName { get; init; } = "SCI-PH";
    }

    public sealed class EmailDeliveryOptionsValidator : IValidateOptions<EmailDeliveryOptions>
    {
        private readonly IHostEnvironment _environment;
        private readonly IOptions<SmtpOptions> _smtp;

        public EmailDeliveryOptionsValidator(IHostEnvironment environment, IOptions<SmtpOptions> smtp)
        {
            _environment = environment;
            _smtp = smtp;
        }

        public ValidateOptionsResult Validate(string? name, EmailDeliveryOptions options)
        {
            var failures = new List<string>();
            switch (options.Mode)
            {
                case EmailDeliveryMode.Smtp:
                    ValidateSmtp(_smtp.Value, failures);
                    break;
                case EmailDeliveryMode.PickupDirectory when _environment.IsProduction():
                    failures.Add("Email:Mode PickupDirectory is not allowed in Production.");
                    break;
                case EmailDeliveryMode.PickupDirectory when !Path.IsPathFullyQualified(options.PickupDirectory ?? string.Empty):
                    failures.Add("Email:PickupDirectory must be an absolute path outside the repository.");
                    break;
                case EmailDeliveryMode.Disabled when _environment.IsProduction():
                    failures.Add("Email:Mode must be Smtp in Production: invitations and password resets need email.");
                    break;
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }

        private static void ValidateSmtp(SmtpOptions smtp, List<string> failures)
        {
            if (string.IsNullOrWhiteSpace(smtp.Host))
            {
                failures.Add("Smtp:Host is required.");
            }

            if (smtp.Port is < 1 or > 65535)
            {
                failures.Add("Smtp:Port must be between 1 and 65535.");
            }

            if (!MailAddress.TryCreate(smtp.FromAddress, out _))
            {
                failures.Add("Smtp:FromAddress must be a valid email address.");
            }

            if (!string.IsNullOrEmpty(smtp.UserName) && string.IsNullOrEmpty(smtp.Password))
            {
                failures.Add("Smtp:Password is required when Smtp:UserName is set (configure it with User Secrets or Smtp__Password).");
            }
        }
    }
}
