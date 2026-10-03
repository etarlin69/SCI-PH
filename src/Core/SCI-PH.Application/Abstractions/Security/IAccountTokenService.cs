using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Abstractions.Security
{
    // Email confirmation and password reset tokens issued by ASP.NET Core Identity.
    public interface IAccountTokenService
    {
        // Active account with this email, or null. Callers must not reveal the result.
        Task<RecoveryTarget?> FindRecoveryTargetAsync(string email, CancellationToken cancellationToken);

        Task<string> CreatePasswordResetTokenAsync(Guid userId, CancellationToken cancellationToken);

        // Renews the security stamp and clears any lockout on success.
        Task<PasswordChangeResult> ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken);

        Task<string> CreateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken);

        Task<bool> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken);
    }
}
