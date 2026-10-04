using Microsoft.AspNetCore.Identity;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.Persistence.Identity.Services
{
    // TOTP (RFC 6238) through the ASP.NET Core Identity authenticator provider. The authenticator key and the
    // recovery codes are managed by Identity in AspNetUserTokens; they are never logged or audited.
    internal sealed class AuthenticatorService : IAuthenticatorService
    {
        public const string Issuer = "SCI-PH";
        private const int RecoveryCodeCount = 10;

        private readonly UserManager<ApplicationUser> _users;

        public AuthenticatorService(UserManager<ApplicationUser> users) => _users = users;

        public async Task<bool> IsEnabledAsync(Guid userId, CancellationToken cancellationToken) =>
            await FindActiveAsync(userId) is { } user && await _users.GetTwoFactorEnabledAsync(user);

        public async Task<MfaSetup?> BeginSetupAsync(Guid userId, CancellationToken cancellationToken)
        {
            var user = await FindActiveAsync(userId);
            if (user is null || await _users.GetTwoFactorEnabledAsync(user))
            {
                return null;
            }

            EnsureSucceeded(await _users.ResetAuthenticatorKeyAsync(user));
            var key = (await _users.GetAuthenticatorKeyAsync(user))!;
            var label = Uri.EscapeDataString($"{Issuer}:{user.Email ?? user.UserName}");
            return new MfaSetup(key, $"otpauth://totp/{label}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6");
        }

        public async Task<RecoveryCodes?> EnableAsync(Guid userId, string code, CancellationToken cancellationToken)
        {
            var user = await FindActiveAsync(userId);
            if (user is null || await _users.GetTwoFactorEnabledAsync(user) || !await IsValidCodeAsync(user, code))
            {
                return null;
            }

            EnsureSucceeded(await _users.SetTwoFactorEnabledAsync(user, true));
            return await GenerateRecoveryCodesAsync(user);
        }

        public async Task<bool> DisableAsync(Guid userId, string code, CancellationToken cancellationToken)
        {
            var user = await FindActiveAsync(userId);
            if (user is null || !await _users.GetTwoFactorEnabledAsync(user) || !await IsValidCodeAsync(user, code))
            {
                return false;
            }

            EnsureSucceeded(await _users.SetTwoFactorEnabledAsync(user, false));
            EnsureSucceeded(await _users.ResetAuthenticatorKeyAsync(user));
            return true;
        }

        public async Task<RecoveryCodes?> RegenerateRecoveryCodesAsync(Guid userId, string code, CancellationToken cancellationToken)
        {
            var user = await FindActiveAsync(userId);
            return user is not null && await _users.GetTwoFactorEnabledAsync(user) && await IsValidCodeAsync(user, code)
                ? await GenerateRecoveryCodesAsync(user)
                : null;
        }

        public Task<MfaCodeCheck> VerifyLoginCodeAsync(Guid userId, string code, CancellationToken cancellationToken) =>
            CheckSecondFactorAsync(userId, user => IsValidCodeAsync(user, code));

        public Task<MfaCodeCheck> RedeemRecoveryCodeAsync(Guid userId, string recoveryCode, CancellationToken cancellationToken) =>
            CheckSecondFactorAsync(userId, async user =>
                (await _users.RedeemTwoFactorRecoveryCodeAsync(user, recoveryCode.Replace(" ", string.Empty, StringComparison.Ordinal))).Succeeded);

        // Failed second factors count towards the same lockout as failed passwords.
        private async Task<MfaCodeCheck> CheckSecondFactorAsync(Guid userId, Func<ApplicationUser, Task<bool>> verify)
        {
            var user = await FindActiveAsync(userId);
            if (user is null || !await _users.GetTwoFactorEnabledAsync(user))
            {
                return MfaCodeCheck.Invalid;
            }

            if (await _users.IsLockedOutAsync(user))
            {
                return MfaCodeCheck.LockedOut;
            }

            if (await verify(user))
            {
                EnsureSucceeded(await _users.ResetAccessFailedCountAsync(user));
                return MfaCodeCheck.Succeeded;
            }

            EnsureSucceeded(await _users.AccessFailedAsync(user));
            return await _users.IsLockedOutAsync(user) ? MfaCodeCheck.LockedOut : MfaCodeCheck.Invalid;
        }

        private Task<bool> IsValidCodeAsync(ApplicationUser user, string code) =>
            _users.VerifyTwoFactorTokenAsync(
                user,
                _users.Options.Tokens.AuthenticatorTokenProvider,
                code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal));

        private async Task<RecoveryCodes> GenerateRecoveryCodesAsync(ApplicationUser user) =>
            new((await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))!.ToArray());

        private async Task<ApplicationUser?> FindActiveAsync(Guid userId) =>
            await _users.FindByIdAsync(userId.ToString()) is { IsActive: true } user ? user : null;

        private static void EnsureSucceeded(IdentityResult result)
        {
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Identity operation failed: {string.Join(", ", result.Errors.Select(error => error.Code))}.");
            }
        }
    }
}
