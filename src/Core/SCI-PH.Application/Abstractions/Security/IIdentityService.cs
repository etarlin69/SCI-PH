using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Abstractions.Security
{
    // Account operations backed by ASP.NET Core Identity. Implementations never expose
    // UserManager, ApplicationUser or the DbContext outside Persistence.
    public interface IIdentityService
    {
        // Applies lockout on failure and records the last login on success.
        Task<CredentialVerification> VerifyCredentialsAsync(string identifier, string password, CancellationToken cancellationToken);

        // Records a completed login (used after a successful MFA step).
        Task RecordSuccessfulLoginAsync(Guid userId, CancellationToken cancellationToken);

        // Returns null when the account does not exist or is inactive.
        Task<UserAccount?> FindActiveAccountAsync(Guid userId, CancellationToken cancellationToken);

        Task<PasswordChangeResult> ChangePasswordAsync(
            Guid userId,
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken);

        // Returns false when the account does not exist.
        Task<bool> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken);
    }
}
