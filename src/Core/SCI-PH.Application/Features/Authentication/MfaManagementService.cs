using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Features.Authentication
{
    // Authenticated MFA management. Enabling or disabling MFA ends every refresh session of the account.
    public sealed class MfaManagementService
    {
        private readonly IAuthenticatorService _authenticator;
        private readonly IRefreshTokenStore _refreshTokens;
        private readonly ISecurityAuditLog _audit;

        public MfaManagementService(IAuthenticatorService authenticator, IRefreshTokenStore refreshTokens, ISecurityAuditLog audit)
        {
            _authenticator = authenticator;
            _refreshTokens = refreshTokens;
            _audit = audit;
        }

        public async Task<MfaSetup?> BeginSetupAsync(Guid userId, ClientContext client, CancellationToken cancellationToken)
        {
            var setup = await _authenticator.BeginSetupAsync(userId, cancellationToken);
            if (setup is not null)
            {
                await _audit.RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.MfaSetupStarted, userId, client), cancellationToken);
            }

            return setup;
        }

        public async Task<RecoveryCodes?> EnableAsync(Guid userId, string code, ClientContext client, CancellationToken cancellationToken)
        {
            var codes = await _authenticator.EnableAsync(userId, code, cancellationToken);
            if (codes is null)
            {
                await RecordInvalidCodeAsync(userId, client, cancellationToken);
                return null;
            }

            await _refreshTokens.RevokeAllForUserAsync(userId, RefreshTokenRevocationReason.MfaChanged, client.IpAddress, cancellationToken);
            await _audit.RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.MfaEnabled, userId, client), cancellationToken);
            return codes;
        }

        public async Task<bool> DisableAsync(Guid userId, string code, ClientContext client, CancellationToken cancellationToken)
        {
            if (!await _authenticator.DisableAsync(userId, code, cancellationToken))
            {
                await RecordInvalidCodeAsync(userId, client, cancellationToken);
                return false;
            }

            await _refreshTokens.RevokeAllForUserAsync(userId, RefreshTokenRevocationReason.MfaChanged, client.IpAddress, cancellationToken);
            await _audit.RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.MfaDisabled, userId, client), cancellationToken);
            return true;
        }

        public async Task<RecoveryCodes?> RegenerateRecoveryCodesAsync(Guid userId, string code, ClientContext client, CancellationToken cancellationToken)
        {
            var codes = await _authenticator.RegenerateRecoveryCodesAsync(userId, code, cancellationToken);
            if (codes is null)
            {
                await RecordInvalidCodeAsync(userId, client, cancellationToken);
                return null;
            }

            await _audit.RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.RecoveryCodesRegenerated, userId, client), cancellationToken);
            return codes;
        }

        private Task RecordInvalidCodeAsync(Guid userId, ClientContext client, CancellationToken cancellationToken) =>
            _audit.RecordAsync(SecurityAuditRecord.Failure(
                SecurityAuditEventType.MfaFailed, userId, SecurityFailureReason.InvalidMfaCode, client), cancellationToken);
    }
}
