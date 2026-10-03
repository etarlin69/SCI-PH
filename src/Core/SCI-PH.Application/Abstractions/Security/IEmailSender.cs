using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Abstractions.Security
{
    // Implementations must never log message bodies: they contain one-time links.
    public interface IEmailSender
    {
        Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
    }
}
