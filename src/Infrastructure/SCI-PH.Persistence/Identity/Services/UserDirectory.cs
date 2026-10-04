using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Persistence.Contexts;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.Persistence.Identity.Services
{
    internal sealed class UserDirectory : IUserDirectory
    {
        private readonly UserManager<ApplicationUser> _users;
        private readonly IdentityDbContext _db;
        private readonly TimeProvider _time;

        public UserDirectory(UserManager<ApplicationUser> users, IdentityDbContext db, TimeProvider time)
        {
            _users = users;
            _db = db;
            _time = time;
        }

        public async Task<UserPage> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
        {
            var query = _db.Users.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var normalized = _users.NormalizeEmail(search.Trim())!;
                query = query.Where(u => u.NormalizedEmail!.Contains(normalized) || u.NormalizedUserName!.Contains(normalized));
            }

            var total = await query.CountAsync(cancellationToken);
            var users = await query
                .OrderBy(u => u.NormalizedUserName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            var roles = await RolesByUserAsync(users.Select(u => u.Id).ToArray(), cancellationToken);

            return new UserPage(users.Select(u => ToSummary(u, roles)).ToArray(), page, pageSize, total);
        }

        public async Task<UserSummary?> GetAsync(Guid userId, CancellationToken cancellationToken)
        {
            var user = await _db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
            return user is null ? null : ToSummary(user, await RolesByUserAsync([user.Id], cancellationToken));
        }

        public async Task<bool> SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
        {
            var user = await _users.FindByIdAsync(userId.ToString());
            if (user is null)
            {
                return false;
            }

            var current = await _users.GetRolesAsync(user);
            EnsureSucceeded(await _users.RemoveFromRolesAsync(user, current.Except(roles)));
            EnsureSucceeded(await _users.AddToRolesAsync(user, roles.Except(current)));
            EnsureSucceeded(await _users.UpdateSecurityStampAsync(user));
            return true;
        }

        public Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken) =>
            (from userRole in _db.UserRoles
             join role in _db.Roles on userRole.RoleId equals role.Id
             join user in _db.Users on userRole.UserId equals user.Id
             where role.Name == SecurityRoles.Administrator && user.IsActive
             select user.Id).CountAsync(cancellationToken);

        public async Task<bool> IsEmailInUseAsync(string email, CancellationToken cancellationToken) =>
            await _users.FindByEmailAsync(email.Trim()) is not null;

        // Created inactive: it becomes usable only after the clinical record is linked.
        public async Task<AccountCreation> CreateInvitedAccountAsync(string email, string password, string role, CancellationToken cancellationToken)
        {
            if (await IsEmailInUseAsync(email, cancellationToken))
            {
                return new AccountCreation(null, true, []);
            }

            var now = _time.GetUtcNow();
            var user = ApplicationUser.Create(email, email, now);
            user.EmailConfirmed = true;
            user.Deactivate(now);

            var created = await _users.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                var duplicate = created.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName));
                return new AccountCreation(null, duplicate, duplicate ? [] : created.Errors.Select(e => e.Description).ToArray());
            }

            var assigned = await _users.AddToRoleAsync(user, role);
            if (!assigned.Succeeded)
            {
                await _users.DeleteAsync(user);
                EnsureSucceeded(assigned);
            }

            return new AccountCreation(user.Id, false, []);
        }

        public async Task DeleteUnusedAccountAsync(Guid userId, CancellationToken cancellationToken)
        {
            var user = await _users.FindByIdAsync(userId.ToString());
            if (user is { LastLoginAtUtc: null })
            {
                EnsureSucceeded(await _users.DeleteAsync(user));
            }
        }

        private async Task<Dictionary<Guid, string[]>> RolesByUserAsync(Guid[] userIds, CancellationToken cancellationToken)
        {
            var pairs = await (
                    from userRole in _db.UserRoles
                    join role in _db.Roles on userRole.RoleId equals role.Id
                    where userIds.Contains(userRole.UserId)
                    select new { userRole.UserId, Role = role.Name! })
                .ToListAsync(cancellationToken);

            return pairs.GroupBy(p => p.UserId)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Role).Order(StringComparer.Ordinal).ToArray());
        }

        private UserSummary ToSummary(ApplicationUser user, Dictionary<Guid, string[]> roles) =>
            new(
                user.Id,
                user.UserName!,
                user.Email,
                user.EmailConfirmed,
                user.IsActive,
                user.TwoFactorEnabled,
                user.LockoutEnd > _time.GetUtcNow() ? user.LockoutEnd : null,
                roles.TryGetValue(user.Id, out var userRoles) ? userRoles : [],
                user.CreatedAtUtc,
                user.LastLoginAtUtc);

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
