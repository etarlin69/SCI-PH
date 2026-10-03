using Microsoft.Extensions.Options;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Features.Accounts
{
    // Password reset and email confirmation. Request methods never reveal whether an email has an account.
    public sealed class AccountRecoveryService
    {
        private readonly IAccountTokenService _accountTokens;
        private readonly IRefreshTokenStore _refreshTokens;
        private readonly IEmailSender _email;
        private readonly ISecurityAuditLog _audit;
        private readonly AccountEmailOptions _links;

        public AccountRecoveryService(
            IAccountTokenService accountTokens,
            IRefreshTokenStore refreshTokens,
            IEmailSender email,
            ISecurityAuditLog audit,
            IOptions<AccountEmailOptions> links)
        {
            _accountTokens = accountTokens;
            _refreshTokens = refreshTokens;
            _email = email;
            _audit = audit;
            _links = links.Value;
        }

        public async Task RequestPasswordResetAsync(string email, ClientContext client, CancellationToken cancellationToken)
        {
            var target = await _accountTokens.FindRecoveryTargetAsync(email, cancellationToken);
            await _audit.RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.PasswordResetRequested, target?.UserId, client), cancellationToken);
            if (target is null)
            {
                return;
            }

            var token = await _accountTokens.CreatePasswordResetTokenAsync(target.UserId, cancellationToken);
            var link = UrlTokens.AppendQuery(_links.ResetPasswordUrl, ("userId", target.UserId.ToString()), ("token", UrlTokens.Encode(token)));
            await TrySendAsync(AccountEmails.PasswordReset(target.Email, link), target.UserId, SecurityAuditEventType.PasswordResetRequested, client, cancellationToken);
        }

        // A successful reset ends every session of the account.
        public async Task<PasswordChangeResult> ResetPasswordAsync(ResetPasswordRequest request, ClientContext client, CancellationToken cancellationToken)
        {
            var token = UrlTokens.Decode(request.Token);
            var result = token is null
                ? PasswordChangeResult.Failed(SecurityFailureReason.InvalidToken)
                : await _accountTokens.ResetPasswordAsync(request.UserId, token, request.NewPassword, cancellationToken);

            if (!result.Succeeded)
            {
                await _audit.RecordAsync(SecurityAuditRecord.Failure(
                    SecurityAuditEventType.PasswordResetFailed, request.UserId, result.FailureReason!.Value, client), cancellationToken);
                return result;
            }

            await _refreshTokens.RevokeAllForUserAsync(request.UserId, RefreshTokenRevocationReason.PasswordReset, client.IpAddress, cancellationToken);
            await _audit.RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.PasswordResetSucceeded, request.UserId, client), cancellationToken);
            return result;
        }

        public async Task RequestEmailConfirmationAsync(string email, ClientContext client, CancellationToken cancellationToken)
        {
            var target = await _accountTokens.FindRecoveryTargetAsync(email, cancellationToken);
            if (target is null || target.EmailConfirmed)
            {
                return;
            }

            await _audit.RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.EmailConfirmationRequested, target.UserId, client), cancellationToken);
            var token = await _accountTokens.CreateEmailConfirmationTokenAsync(target.UserId, cancellationToken);
            var link = UrlTokens.AppendQuery(_links.ConfirmEmailUrl, ("userId", target.UserId.ToString()), ("token", UrlTokens.Encode(token)));
            await TrySendAsync(AccountEmails.ConfirmEmail(target.Email, link), target.UserId, SecurityAuditEventType.EmailConfirmationRequested, client, cancellationToken);
        }

        public async Task<bool> ConfirmEmailAsync(ConfirmEmailRequest request, ClientContext client, CancellationToken cancellationToken)
        {
            var token = UrlTokens.Decode(request.Token);
            var confirmed = token is not null && await _accountTokens.ConfirmEmailAsync(request.UserId, token, cancellationToken);
            await _audit.RecordAsync(confirmed
                ? SecurityAuditRecord.Success(SecurityAuditEventType.EmailConfirmed, request.UserId, client)
                : SecurityAuditRecord.Failure(SecurityAuditEventType.EmailConfirmed, request.UserId, SecurityFailureReason.InvalidToken, client), cancellationToken);
            return confirmed;
        }

        // Delivery failures are audited but never surfaced: the response must stay the same for every email.
        private async Task TrySendAsync(
            EmailMessage message,
            Guid userId,
            SecurityAuditEventType eventType,
            ClientContext client,
            CancellationToken cancellationToken)
        {
            try
            {
                await _email.SendAsync(message, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await _audit.RecordAsync(SecurityAuditRecord.Failure(eventType, userId, SecurityFailureReason.EmailDeliveryFailed, client), cancellationToken);
            }
        }
    }
}
