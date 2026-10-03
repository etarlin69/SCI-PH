namespace SCI_PH.Application.Common.Security
{
    // Expiration is evaluated from ExpiresAtUtc; it is not stored as a status.
    public enum AccountInvitationStatus
    {
        Pending = 1,
        Accepted = 2,
        Revoked = 3
    }
}
