using System.Collections.Concurrent;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.IntegrationTests.Security.Support
{
    // Keeps messages in memory; no email ever leaves the test process.
    internal sealed class FakeEmailSender : IEmailSender
    {
        private readonly ConcurrentQueue<EmailMessage> _sent = new();

        public bool Fail { get; set; }

        public IReadOnlyList<EmailMessage> Sent => _sent.ToArray();

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("Simulated delivery failure.");
            }

            _sent.Enqueue(message);
            return Task.CompletedTask;
        }

        public EmailMessage LastTo(string email) => Sent.Last(m => m.To.Equals(email, StringComparison.OrdinalIgnoreCase));

        // Reads a query parameter from the link in the message body.
        public static string LinkParameter(EmailMessage message, string name)
        {
            var link = message.TextBody.Split('\n').Select(line => line.Trim()).First(line => line.StartsWith("http", StringComparison.Ordinal));
            var query = new Uri(link).Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2));
            return Uri.UnescapeDataString(query.First(pair => pair[0] == name)[1]);
        }
    }
}
