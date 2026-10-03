using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SCI_PH.Application.Common.Security;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.Persistence.Identity.Seed
{
    // Idempotent: creates missing roles, aligns their permission claims with RolePermissions and,
    // only when configured externally, creates the first administrator. Never contains credentials.
    public sealed class IdentitySeeder
    {
        private readonly RoleManager<IdentityRole<Guid>> _roles;
        private readonly UserManager<ApplicationUser> _users;
        private readonly IdentityBootstrapOptions _options;
        private readonly TimeProvider _time;
        private readonly ILogger<IdentitySeeder> _logger;

        public IdentitySeeder(
            RoleManager<IdentityRole<Guid>> roles,
            UserManager<ApplicationUser> users,
            IOptions<IdentityBootstrapOptions> options,
            TimeProvider time,
            ILogger<IdentitySeeder> logger)
        {
            _roles = roles;
            _users = users;
            _options = options.Value;
            _time = time;
            _logger = logger;
        }

        public async Task SeedAsync(CancellationToken cancellationToken = default)
        {
            foreach (var roleName in SecurityRoles.All)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var role = await _roles.FindByNameAsync(roleName) ?? await CreateRoleAsync(roleName);
                await SynchronizePermissionsAsync(role, RolePermissions.For(roleName));
            }

            if (_options.HasAdministrator)
            {
                await EnsureAdministratorAsync(_options.AdminEmail!.Trim(), _options.AdminPassword!);
            }
        }

        private async Task<IdentityRole<Guid>> CreateRoleAsync(string roleName)
        {
            var role = new IdentityRole<Guid>(roleName) { Id = Guid.NewGuid() };
            EnsureSucceeded(await _roles.CreateAsync(role));
            return role;
        }

        private async Task SynchronizePermissionsAsync(IdentityRole<Guid> role, IReadOnlyList<string> expected)
        {
            var current = (await _roles.GetClaimsAsync(role))
                .Where(claim => claim.Type == CustomClaimTypes.Permission)
                .ToList();

            foreach (var obsolete in current.Where(claim => !expected.Contains(claim.Value)))
            {
                EnsureSucceeded(await _roles.RemoveClaimAsync(role, obsolete));
            }

            foreach (var missing in expected.Except(current.Select(claim => claim.Value)))
            {
                EnsureSucceeded(await _roles.AddClaimAsync(role, new Claim(CustomClaimTypes.Permission, missing)));
            }
        }

        private async Task EnsureAdministratorAsync(string email, string password)
        {
            var user = await _users.FindByEmailAsync(email);
            if (user is null)
            {
                user = ApplicationUser.Create(email, email, _time.GetUtcNow());
                EnsureSucceeded(await _users.CreateAsync(user, password));
                _logger.LogInformation("Bootstrap administrator account {UserId} created.", user.Id);
            }

            if (!await _users.IsInRoleAsync(user, SecurityRoles.Administrator))
            {
                EnsureSucceeded(await _users.AddToRoleAsync(user, SecurityRoles.Administrator));
            }
        }

        // Error codes only: Identity descriptions could echo configuration values.
        private static void EnsureSucceeded(IdentityResult result)
        {
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Identity seeding failed: {string.Join(", ", result.Errors.Select(error => error.Code))}.");
            }
        }
    }
}
