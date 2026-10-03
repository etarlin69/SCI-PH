using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Features.Accounts
{
    // Account administration (Users.Manage). Admins assign predefined roles only; the role -> permission
    // matrix stays in code. Every security change ends the account sessions.
    public sealed class UserAccountService
    {
        public const int MaxPageSize = 100;

        private readonly IIdentityService _identity;
        private readonly IUserDirectory _directory;
        private readonly IRefreshTokenStore _refreshTokens;
        private readonly ISecurityAuditLog _audit;

        public UserAccountService(
            IIdentityService identity,
            IUserDirectory directory,
            IRefreshTokenStore refreshTokens,
            ISecurityAuditLog audit)
        {
            _identity = identity;
            _directory = directory;
            _refreshTokens = refreshTokens;
            _audit = audit;
        }

        public Task<UserPage> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken) =>
            _directory.ListAsync(search, Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize), cancellationToken);

        public Task<UserSummary?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
            _directory.GetAsync(userId, cancellationToken);

        public async Task<UserAdministrationOutcome> DeactivateAsync(Guid userId, Guid actorUserId, ClientContext client, CancellationToken cancellationToken)
        {
            var target = await _directory.GetAsync(userId, cancellationToken);
            var guard = await GuardAdministratorLossAsync(target, actorUserId, losesAdministrator: true, cancellationToken);
            if (guard != UserAdministrationOutcome.Succeeded)
            {
                return guard;
            }

            await _identity.SetActiveAsync(userId, isActive: false, cancellationToken);
            await _refreshTokens.RevokeAllForUserAsync(userId, RefreshTokenRevocationReason.AccountDeactivated, client.IpAddress, cancellationToken);
            await RecordAsync(SecurityAuditEventType.AccountDeactivated, userId, actorUserId, client, cancellationToken);
            return UserAdministrationOutcome.Succeeded;
        }

        public async Task<UserAdministrationOutcome> ActivateAsync(Guid userId, Guid actorUserId, ClientContext client, CancellationToken cancellationToken)
        {
            if (!await _identity.SetActiveAsync(userId, isActive: true, cancellationToken))
            {
                return UserAdministrationOutcome.NotFound;
            }

            await RecordAsync(SecurityAuditEventType.AccountActivated, userId, actorUserId, client, cancellationToken);
            return UserAdministrationOutcome.Succeeded;
        }

        // Roles must be predefined; Patient cannot be combined with staff roles.
        public async Task<UserAdministrationOutcome> SetRolesAsync(
            Guid userId,
            IReadOnlyCollection<string> roles,
            Guid actorUserId,
            ClientContext client,
            CancellationToken cancellationToken)
        {
            var requested = roles.Distinct(StringComparer.Ordinal).ToArray();
            if (requested.Any(role => !SecurityRoles.All.Contains(role))
                || (requested.Contains(SecurityRoles.Patient) && requested.Length > 1))
            {
                return UserAdministrationOutcome.InvalidRoles;
            }

            var target = await _directory.GetAsync(userId, cancellationToken);
            var guard = await GuardAdministratorLossAsync(
                target, actorUserId, losesAdministrator: !requested.Contains(SecurityRoles.Administrator), cancellationToken);
            if (guard != UserAdministrationOutcome.Succeeded)
            {
                return guard;
            }

            await _directory.SetRolesAsync(userId, requested, cancellationToken);
            await _refreshTokens.RevokeAllForUserAsync(userId, RefreshTokenRevocationReason.RolesChanged, client.IpAddress, cancellationToken);
            await RecordAsync(SecurityAuditEventType.RoleChanged, userId, actorUserId, client, cancellationToken);
            return UserAdministrationOutcome.Succeeded;
        }

        public async Task<UserAdministrationOutcome> RevokeSessionsAsync(Guid userId, Guid actorUserId, ClientContext client, CancellationToken cancellationToken)
        {
            if (await _directory.GetAsync(userId, cancellationToken) is null)
            {
                return UserAdministrationOutcome.NotFound;
            }

            await _refreshTokens.RevokeAllForUserAsync(userId, RefreshTokenRevocationReason.SessionsRevoked, client.IpAddress, cancellationToken);
            await RecordAsync(SecurityAuditEventType.SessionsRevoked, userId, actorUserId, client, cancellationToken);
            return UserAdministrationOutcome.Succeeded;
        }

        // An administrator cannot demote or deactivate themselves, and the last active administrator is protected.
        private async Task<UserAdministrationOutcome> GuardAdministratorLossAsync(
            UserSummary? target,
            Guid actorUserId,
            bool losesAdministrator,
            CancellationToken cancellationToken)
        {
            if (target is null)
            {
                return UserAdministrationOutcome.NotFound;
            }

            if (target.Id == actorUserId && losesAdministrator)
            {
                return UserAdministrationOutcome.SelfModification;
            }

            var isActiveAdministrator = target.IsActive && target.Roles.Contains(SecurityRoles.Administrator);
            return isActiveAdministrator && losesAdministrator && await _directory.CountActiveAdministratorsAsync(cancellationToken) <= 1
                ? UserAdministrationOutcome.LastAdministrator
                : UserAdministrationOutcome.Succeeded;
        }

        private Task RecordAsync(SecurityAuditEventType eventType, Guid userId, Guid actorUserId, ClientContext client, CancellationToken cancellationToken) =>
            _audit.RecordAsync(SecurityAuditRecord.Success(eventType, userId, client) with { ActorUserId = actorUserId }, cancellationToken);
    }
}
