using Microsoft.AspNetCore.Identity;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.Persistence.Identity.Services
{
    // Password reset and email confirmation tokens from ASP.NET Core Identity (Data Protection based,
    // bound to the security stamp). Tokens are returned to Application only to build the email link.
    internal sealed class AccountTokenService : IAccountTokenService
    {
        private readonly UserManager<ApplicationUser> _users;

        public AccountTokenService(UserManager<ApplicationUser> users) => _users = users;

        public async Task<RecoveryTarget?> FindRecoveryTargetAsync(string email, CancellationToken cancellationToken)
        {
            var user = await _users.FindByEmailAsync(email.Trim());
            return user is { IsActive: true, Email: not null }
                ? new RecoveryTarget(user.Id, user.Email, user.EmailConfirmed)
                : null;
        }

        public async Task<string> CreatePasswordResetTokenAsync(Guid userId, CancellationToken cancellationToken) =>
            await _users.GeneratePasswordResetTokenAsync(await FindRequiredAsync(userId));

        public async Task<PasswordChangeResult> ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken)
        {
            var user = await _users.FindByIdAsync(userId.ToString());
            if (user is not { IsActive: true })
            {
                return PasswordChangeResult.Failed(SecurityFailureReason.InvalidToken);
            }

            var result = await _users.ResetPasswordAsync(user, token, newPassword);
            if (result.Succeeded)
            {
                // Proving control of the mailbox also lifts a lockout caused by failed attempts.
                await _users.SetLockoutEndDateAsync(user, null);
                await _users.ResetAccessFailedCountAsync(user);
                return PasswordChangeResult.Success;
            }

            var invalidTokenCode = _users.ErrorDescriber.InvalidToken().Code;
            return result.Errors.Any(error => error.Code == invalidTokenCode)
                ? PasswordChangeResult.Failed(SecurityFailureReason.InvalidToken)
                : PasswordChangeResult.Failed(SecurityFailureReason.PasswordPolicy, result.Errors.Select(e => e.Description).ToArray());
        }

        public async Task<string> CreateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken) =>
            await _users.GenerateEmailConfirmationTokenAsync(await FindRequiredAsync(userId));

        public async Task<bool> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken)
        {
            var user = await _users.FindByIdAsync(userId.ToString());
            return user is { IsActive: true } && (await _users.ConfirmEmailAsync(user, token)).Succeeded;
        }

        private async Task<ApplicationUser> FindRequiredAsync(Guid userId) =>
            await _users.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("The account does not exist.");
    }
}
