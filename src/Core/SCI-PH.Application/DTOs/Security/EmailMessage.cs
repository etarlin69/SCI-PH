namespace SCI_PH.Application.DTOs.Security
{
    // Plain-text email. Bodies may contain one-time links, so senders must never log them.
    public sealed record EmailMessage(string To, string Subject, string TextBody);
}
