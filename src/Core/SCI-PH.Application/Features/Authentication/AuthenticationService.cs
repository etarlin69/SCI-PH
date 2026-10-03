using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Features.Authentication
{
    // Orchestrates login, refresh rotation, logout and password changes for every BFF.
    // Methods return null for any authentication failure so callers can only answer with a generic error.
    public sealed class AuthenticationService
    {
        private readonly IIdentityService _identity;
        private readonly ITokenService _tokens;
        private readonly IRefreshTokenStore _refreshTokens;
        private readonly ISecurityAuditLog _audit;
        private readonly IAuthenticatorService _authenticator;
        private readonly IMfaChallengeStore _mfaChallenges;
        private readonly ISecureTokenGenerator _secrets;
        private readonly TimeProvider _time;

        // 256-bit challenge identifier (JWT jti); only its SHA-256 hash is stored.
        private const int MfaChallengeIdBytes = 32;

        public AuthenticationService(
            IIdentityService identity,
            ITokenService tokens,
            IRefreshTokenStore refreshTokens,
            ISecurityAuditLog audit,
            IAuthenticatorService authenticator,
            IMfaChallengeStore mfaChallenges,
            ISecureTokenGenerator secrets,
            TimeProvider time)
        {
            _identity = identity;
            _tokens = tokens;
            _refreshTokens = refreshTokens;
            _audit = audit;
            _authenticator = authenticator;
            _mfaChallenges = mfaChallenges;
            _secrets = secrets;
            _time = time;
        }

        // With MFA enabled a correct password only yields a short challenge; the session is issued by VerifyMfaAsync.
        public async Task<LoginResult?> LoginAsync(LoginRequest request, ClientContext client, CancellationToken cancellationToken)
        {
            var verification = await _identity.VerifyCredentialsAsync(request.Identifier, request.Password, cancellationToken);
            if (verification.Status == CredentialStatus.RequiresTwoFactor)
            {
                return LoginResult.WithChallenge(await IssueMfaChallengeAsync(verification.UserId!.Value, cancellationToken));
            }

            if (verification.Status != CredentialStatus.Succeeded)
            {
                await RecordLoginFailureAsync(verification, client, cancellationToken);
                return null;
            }

            var account = verification.Account!;
            var result = await StartSessionAsync(account, client, cancellationToken);
            await RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.LoginSucceeded, account.Id, client), cancellationToken);
            return LoginResult.WithSession(result);
        }

        // Second login step: a valid challenge plus either an authenticator code or a recovery code.
        // Each challenge yields at most one session. It is claimed atomically BEFORE the code is checked, so two
        // concurrent requests can never both pass; a wrong code releases the claim (the challenge stays usable
        // and the failure counts towards lockout), a correct code leaves it consumed.
        public async Task<AuthenticationResult?> VerifyMfaAsync(MfaVerifyRequest request, ClientContext client, CancellationToken cancellationToken)
        {
            var challenge = await _tokens.ReadMfaChallengeAsync(request.ChallengeToken);
            var useRecoveryCode = !string.IsNullOrWhiteSpace(request.RecoveryCode);
            var claimedId = challenge is null || useRecoveryCode == !string.IsNullOrWhiteSpace(request.Code)
                ? null
                : await _mfaChallenges.TryClaimAsync(challenge.UserId, _secrets.Hash(challenge.ChallengeId), cancellationToken);
            if (claimedId is null)
            {
                await RecordAsync(SecurityAuditRecord.Failure(
                    SecurityAuditEventType.MfaFailed, challenge?.UserId, SecurityFailureReason.InvalidToken, client), cancellationToken);
                return null;
            }

            var userId = challenge!.UserId;
            var check = useRecoveryCode
                ? await _authenticator.RedeemRecoveryCodeAsync(userId, request.RecoveryCode!, cancellationToken)
                : await _authenticator.VerifyLoginCodeAsync(userId, request.Code!, cancellationToken);
            if (check != MfaCodeCheck.Succeeded)
            {
                await _mfaChallenges.ReleaseAsync(claimedId.Value, cancellationToken);
                var reason = check == MfaCodeCheck.LockedOut ? SecurityFailureReason.LockedOut : SecurityFailureReason.InvalidMfaCode;
                await RecordAsync(SecurityAuditRecord.Failure(SecurityAuditEventType.MfaFailed, userId, reason, client), cancellationToken);
                return null;
            }

            var account = await _identity.FindActiveAccountAsync(userId, cancellationToken);
            if (account is null)
            {
                await RecordAsync(SecurityAuditRecord.Failure(
                    SecurityAuditEventType.MfaFailed, userId, SecurityFailureReason.AccountInactive, client), cancellationToken);
                return null;
            }

            await _identity.RecordSuccessfulLoginAsync(account.Id, cancellationToken);
            var result = await StartSessionAsync(account, client, cancellationToken);
            await RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.MfaSucceeded, account.Id, client), cancellationToken);
            if (useRecoveryCode)
            {
                await RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.RecoveryCodeUsed, account.Id, client), cancellationToken);
            }

            await RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.LoginSucceeded, account.Id, client), cancellationToken);
            return result;
        }

        public async Task<AuthenticationResult?> RefreshAsync(string refreshToken, ClientContext client, CancellationToken cancellationToken)
        {
            var stored = await _refreshTokens.FindByHashAsync(_tokens.HashRefreshToken(refreshToken), cancellationToken);
            var failure = await ValidateForRefreshAsync(stored, client, cancellationToken);
            if (failure is not null)
            {
                await RecordAsync(SecurityAuditRecord.Failure(SecurityAuditEventType.RefreshFailed, stored?.UserId, failure.Value, client), cancellationToken);
                return null;
            }

            var account = await _identity.FindActiveAccountAsync(stored!.UserId, cancellationToken);
            if (account is null)
            {
                await _refreshTokens.RevokeFamilyAsync(stored.FamilyId, RefreshTokenRevocationReason.AccountDeactivated, client.IpAddress, cancellationToken);
                await RecordAsync(SecurityAuditRecord.Failure(
                    SecurityAuditEventType.RefreshFailed, stored.UserId, SecurityFailureReason.AccountInactive, client), cancellationToken);
                return null;
            }

            var replacement = _tokens.CreateRefreshToken();
            var rotated = await _refreshTokens.RotateAsync(
                stored.Id, ToNewToken(account.Id, stored.FamilyId, replacement, client), client.IpAddress, cancellationToken);
            if (!rotated)
            {
                await RecordAsync(SecurityAuditRecord.Failure(
                    SecurityAuditEventType.RefreshFailed, account.Id, SecurityFailureReason.TokenRevoked, client), cancellationToken);
                return null;
            }

            await RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.RefreshSucceeded, account.Id, client), cancellationToken);
            return BuildResult(account, replacement);
        }

        // Idempotent: unknown or already revoked tokens are ignored.
        public async Task LogoutAsync(string refreshToken, ClientContext client, CancellationToken cancellationToken)
        {
            var stored = await _refreshTokens.FindByHashAsync(_tokens.HashRefreshToken(refreshToken), cancellationToken);
            if (stored is null || stored.IsRevoked)
            {
                return;
            }

            await _refreshTokens.RevokeFamilyAsync(stored.FamilyId, RefreshTokenRevocationReason.Logout, client.IpAddress, cancellationToken);
            await RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.Logout, stored.UserId, client), cancellationToken);
        }

        public Task<UserAccount?> GetCurrentAccountAsync(Guid userId, CancellationToken cancellationToken) =>
            _identity.FindActiveAccountAsync(userId, cancellationToken);

        public async Task<PasswordChangeResult> ChangePasswordAsync(
            Guid userId,
            ChangePasswordRequest request,
            ClientContext client,
            CancellationToken cancellationToken)
        {
            var result = await _identity.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword, cancellationToken);
            if (!result.Succeeded)
            {
                await RecordAsync(SecurityAuditRecord.Failure(
                    SecurityAuditEventType.PasswordChangeFailed, userId, result.FailureReason!.Value, client), cancellationToken);
                return result;
            }

            await _refreshTokens.RevokeAllForUserAsync(userId, RefreshTokenRevocationReason.PasswordChanged, client.IpAddress, cancellationToken);
            await RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.PasswordChanged, userId, client), cancellationToken);
            return result;
        }

        public async Task RevokeAllSessionsAsync(Guid userId, ClientContext client, CancellationToken cancellationToken)
        {
            await _refreshTokens.RevokeAllForUserAsync(userId, RefreshTokenRevocationReason.SessionsRevoked, client.IpAddress, cancellationToken);
            await RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.SessionsRevoked, userId, client), cancellationToken);
        }

        private async Task<MfaChallenge> IssueMfaChallengeAsync(Guid userId, CancellationToken cancellationToken)
        {
            var challengeId = _secrets.Generate(MfaChallengeIdBytes);
            var challenge = _tokens.CreateMfaChallenge(userId, challengeId);
            await _mfaChallenges.AddAsync(userId, _secrets.Hash(challengeId), challenge.ExpiresAtUtc, cancellationToken);
            return challenge;
        }

        private async Task<AuthenticationResult> StartSessionAsync(UserAccount account, ClientContext client, CancellationToken cancellationToken)
        {
            var refreshToken = _tokens.CreateRefreshToken();
            await _refreshTokens.AddAsync(ToNewToken(account.Id, Guid.NewGuid(), refreshToken, client), cancellationToken);
            return BuildResult(account, refreshToken);
        }

        // Returns the failure reason, or null when the token can be rotated.
        // Presenting an already rotated token means it was copied: the whole session family is revoked.
        private async Task<SecurityFailureReason?> ValidateForRefreshAsync(
            StoredRefreshToken? stored,
            ClientContext client,
            CancellationToken cancellationToken)
        {
            if (stored is null)
            {
                return SecurityFailureReason.TokenNotFound;
            }

            if (stored.RevocationReason == RefreshTokenRevocationReason.Rotated)
            {
                await _refreshTokens.RevokeFamilyAsync(stored.FamilyId, RefreshTokenRevocationReason.ReuseDetected, client.IpAddress, cancellationToken);
                await RecordAsync(SecurityAuditRecord.Failure(
                    SecurityAuditEventType.RefreshTokenReuseDetected, stored.UserId, SecurityFailureReason.TokenReused, client), cancellationToken);
                return SecurityFailureReason.TokenReused;
            }

            if (stored.IsRevoked)
            {
                return SecurityFailureReason.TokenRevoked;
            }

            return stored.ExpiresAtUtc <= _time.GetUtcNow() ? SecurityFailureReason.TokenExpired : null;
        }

        private async Task RecordLoginFailureAsync(CredentialVerification verification, ClientContext client, CancellationToken cancellationToken)
        {
            var reason = verification.Status switch
            {
                CredentialStatus.LockedOut => SecurityFailureReason.LockedOut,
                CredentialStatus.Inactive => SecurityFailureReason.AccountInactive,
                _ => verification.UserId is null ? SecurityFailureReason.AccountNotFound : SecurityFailureReason.InvalidCredentials
            };

            await RecordAsync(SecurityAuditRecord.Failure(SecurityAuditEventType.LoginFailed, verification.UserId, reason, client), cancellationToken);
            if (verification.LockoutTriggered)
            {
                await RecordAsync(SecurityAuditRecord.Success(SecurityAuditEventType.AccountLocked, verification.UserId, client), cancellationToken);
            }
        }

        private AuthenticationResult BuildResult(UserAccount account, GeneratedRefreshToken refreshToken)
        {
            var accessToken = _tokens.CreateAccessToken(account);
            return new AuthenticationResult(
                accessToken.Value, accessToken.ExpiresAtUtc, refreshToken.Value, refreshToken.ExpiresAtUtc, account);
        }

        private static NewRefreshToken ToNewToken(Guid userId, Guid familyId, GeneratedRefreshToken token, ClientContext client) =>
            new(userId, familyId, token.Hash, token.ExpiresAtUtc, client.IpAddress);

        private Task RecordAsync(SecurityAuditRecord record, CancellationToken cancellationToken) =>
            _audit.RecordAsync(record, cancellationToken);
    }
}
