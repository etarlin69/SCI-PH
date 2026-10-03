namespace SCI_PH.Application.DTOs.Security
{
    public enum CredentialStatus
    {
        Succeeded = 1,
        InvalidCredentials = 2,
        LockedOut = 3,
        Inactive = 4,
        // Password was correct but the account requires a second factor before any session is issued.
        RequiresTwoFactor = 5
    }

    // UserId is known whenever the account exists; Account is set only on success.
    // LockoutTriggered is true when this failed attempt locked the account.
    public sealed record CredentialVerification(
        CredentialStatus Status,
        Guid? UserId,
        UserAccount? Account,
        bool LockoutTriggered = false);
}
