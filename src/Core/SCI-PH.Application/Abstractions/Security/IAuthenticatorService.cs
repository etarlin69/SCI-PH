using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Abstractions.Security
{
    // TOTP authenticator and recovery codes backed by ASP.NET Core Identity.
    public interface IAuthenticatorService
    {
        Task<bool> IsEnabledAsync(Guid userId, CancellationToken cancellationToken);

        // Returns null when the account does not exist, is inactive or already has MFA enabled.
        Task<MfaSetup?> BeginSetupAsync(Guid userId, CancellationToken cancellationToken);

        // Verifies the first code and enables MFA. Returns new recovery codes, or null when the code is invalid.
        Task<RecoveryCodes?> EnableAsync(Guid userId, string code, CancellationToken cancellationToken);

        Task<bool> DisableAsync(Guid userId, string code, CancellationToken cancellationToken);

        Task<RecoveryCodes?> RegenerateRecoveryCodesAsync(Guid userId, string code, CancellationToken cancellationToken);

        // Login second factor. Failures count towards the account lockout.
        Task<MfaCodeCheck> VerifyLoginCodeAsync(Guid userId, string code, CancellationToken cancellationToken);

        Task<MfaCodeCheck> RedeemRecoveryCodeAsync(Guid userId, string recoveryCode, CancellationToken cancellationToken);
    }
}
