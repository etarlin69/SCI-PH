using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Persistence.Contexts;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.Persistence.Identity.Services
{
    internal sealed class IdentityService : IIdentityService
    {
        // Unknown accounts still pay for a hash verification so response time does not reveal whether they exist.
        private static readonly PasswordHasher<ApplicationUser> _timingHasher = new();
        private static readonly Lazy<string> _timingHash = new(() =>
            _timingHasher.HashPassword(new ApplicationUser(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

        private readonly UserManager<ApplicationUser> _users;
        private readonly IdentityDbContext _db;
        private readonly TimeProvider _time;

        public IdentityService(UserManager<ApplicationUser> users, IdentityDbContext db, TimeProvider time)
        {
            _users = users;
            _db = db;
            _time = time;
        }

        public async Task<CredentialVerification> VerifyCredentialsAsync(string identifier, string password, CancellationToken cancellationToken)
        {
            var user = await FindByIdentifierAsync(identifier);
            if (user is null)
            {
                _timingHasher.VerifyHashedPassword(new ApplicationUser(), _timingHash.Value, password);
                return new CredentialVerification(CredentialStatus.InvalidCredentials, null, null);
            }

            if (!user.IsActive)
            {
                return new CredentialVerification(CredentialStatus.Inactive, user.Id, null);
            }

            if (await _users.IsLockedOutAsync(user))
            {
                return new CredentialVerification(CredentialStatus.LockedOut, user.Id, null);
            }

            if (!await _users.CheckPasswordAsync(user, password))
            {
                return await RegisterFailedAttemptAsync(user);
            }

            await _users.ResetAccessFailedCountAsync(user);
            if (await _users.GetTwoFactorEnabledAsync(user))
            {
                // The login is completed (and recorded) only after the second factor.
                return new CredentialVerification(CredentialStatus.RequiresTwoFactor, user.Id, null);
            }

            await RecordLoginAsync(user);
            return new CredentialVerification(CredentialStatus.Succeeded, user.Id, await BuildAccountAsync(user, cancellationToken));
        }

        public async Task RecordSuccessfulLoginAsync(Guid userId, CancellationToken cancellationToken)
        {
            var user = await _users.FindByIdAsync(userId.ToString());
            if (user is not null)
            {
                await RecordLoginAsync(user);
            }
        }

        public async Task<UserAccount?> FindActiveAccountAsync(Guid userId, CancellationToken cancellationToken)
        {
            var user = await _users.FindByIdAsync(userId.ToString());
            return user is { IsActive: true } ? await BuildAccountAsync(user, cancellationToken) : null;
        }

        // UserManager.ChangePasswordAsync also renews the security stamp.
        public async Task<PasswordChangeResult> ChangePasswordAsync(
            Guid userId,
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken)
        {
            var user = await _users.FindByIdAsync(userId.ToString());
            if (user is not { IsActive: true })
            {
                return PasswordChangeResult.Failed(SecurityFailureReason.AccountInactive);
            }

            var result = await _users.ChangePasswordAsync(user, currentPassword, newPassword);
            if (result.Succeeded)
            {
                return PasswordChangeResult.Success;
            }

            var mismatchCode = _users.ErrorDescriber.PasswordMismatch().Code;
            return result.Errors.Any(error => error.Code == mismatchCode)
                ? PasswordChangeResult.Failed(SecurityFailureReason.PasswordMismatch)
                : PasswordChangeResult.Failed(SecurityFailureReason.PasswordPolicy, result.Errors.Select(e => e.Description).ToArray());
        }

        public async Task<bool> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
        {
            var user = await _users.FindByIdAsync(userId.ToString());
            if (user is null)
            {
                return false;
            }

            if (isActive)
            {
                user.Activate();
            }
            else
            {
                user.Deactivate(_time.GetUtcNow());
            }

            EnsureSucceeded(await _users.UpdateSecurityStampAsync(user));
            return true;
        }

        private async Task<ApplicationUser?> FindByIdentifierAsync(string identifier)
        {
            var value = identifier.Trim();
            var user = value.Contains('@') ? await _users.FindByEmailAsync(value) : null;
            return user ?? await _users.FindByNameAsync(value);
        }

        private async Task RecordLoginAsync(ApplicationUser user)
        {
            user.RecordLogin(_time.GetUtcNow());
            EnsureSucceeded(await _users.UpdateAsync(user));
        }

        private async Task<CredentialVerification> RegisterFailedAttemptAsync(ApplicationUser user)
        {
            EnsureSucceeded(await _users.AccessFailedAsync(user));
            var lockedOut = await _users.IsLockedOutAsync(user);
            return new CredentialVerification(
                lockedOut ? CredentialStatus.LockedOut : CredentialStatus.InvalidCredentials, user.Id, null, lockedOut);
        }

        private async Task<UserAccount> BuildAccountAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            var roles = await (
                    from userRole in _db.UserRoles
                    join role in _db.Roles on userRole.RoleId equals role.Id
                    where userRole.UserId == user.Id
                    select role.Name!)
                .ToListAsync(cancellationToken);

            var rolePermissions = await (
                    from userRole in _db.UserRoles
                    join claim in _db.RoleClaims on userRole.RoleId equals claim.RoleId
                    where userRole.UserId == user.Id && claim.ClaimType == CustomClaimTypes.Permission
                    select claim.ClaimValue!)
                .ToListAsync(cancellationToken);

            var userPermissions = await _db.UserClaims
                .Where(claim => claim.UserId == user.Id && claim.ClaimType == CustomClaimTypes.Permission)
                .Select(claim => claim.ClaimValue!)
                .ToListAsync(cancellationToken);

            return new UserAccount(
                user.Id,
                user.UserName!,
                user.Email,
                roles.Order(StringComparer.Ordinal).ToArray(),
                rolePermissions.Concat(userPermissions).Distinct().Order(StringComparer.Ordinal).ToArray());
        }

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
