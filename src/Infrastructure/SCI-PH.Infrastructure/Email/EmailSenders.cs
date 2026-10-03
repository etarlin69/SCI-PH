using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Infrastructure.Email
{
    // None of these senders log recipients or bodies: bodies contain one-time links.
    internal static class MimeMessages
    {
        public const string DefaultFromAddress = "no-reply@sci-ph.invalid";

        public static MimeMessage Create(EmailMessage message, SmtpOptions smtp)
        {
            var from = string.IsNullOrWhiteSpace(smtp.FromAddress) ? DefaultFromAddress : smtp.FromAddress;
            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(smtp.FromName, from));
            mime.To.Add(MailboxAddress.Parse(message.To));
            mime.Subject = message.Subject;
            var body = new TextPart("plain");
            body.SetText(System.Text.Encoding.UTF8, message.TextBody);
            mime.Body = body;
            return mime;
        }
    }

    internal sealed class SmtpEmailSender : IEmailSender
    {
        private readonly SmtpOptions _smtp;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IOptions<SmtpOptions> smtp, ILogger<SmtpEmailSender> logger)
        {
            _smtp = smtp.Value;
            _logger = logger;
        }

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            using var client = new SmtpClient();
            try
            {
                // Never plaintext: either implicit TLS or mandatory STARTTLS.
                await client.ConnectAsync(_smtp.Host, _smtp.Port,
                    _smtp.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, cancellationToken);
                if (!string.IsNullOrEmpty(_smtp.UserName))
                {
                    await client.AuthenticateAsync(_smtp.UserName, _smtp.Password ?? string.Empty, cancellationToken);
                }

                await client.SendAsync(MimeMessages.Create(message, _smtp), cancellationToken);
                await client.DisconnectAsync(quit: true, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning("SMTP delivery failed ({ExceptionType}).", exception.GetType().Name);
                throw new InvalidOperationException("Email delivery failed.");
            }
        }
    }

    internal sealed class PickupDirectoryEmailSender : IEmailSender
    {
        private readonly string _directory;
        private readonly SmtpOptions _smtp;

        public PickupDirectoryEmailSender(IOptions<EmailDeliveryOptions> options, IOptions<SmtpOptions> smtp)
        {
            _directory = options.Value.PickupDirectory!;
            _smtp = smtp.Value;
        }

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.eml");
            await MimeMessages.Create(message, _smtp).WriteToAsync(path, cancellationToken);
        }
    }

    internal sealed class DisabledEmailSender : IEmailSender
    {
        private readonly ILogger<DisabledEmailSender> _logger;

        public DisabledEmailSender(ILogger<DisabledEmailSender> logger) => _logger = logger;

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Email delivery is disabled; message '{Subject}' was not sent.", message.Subject);
            return Task.CompletedTask;
        }
    }
}
