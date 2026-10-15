using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SCI_PH.Application.Common.Security;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.Persistence.Identity.Seed
{
    // Idempotent: creates each configured account once and assigns its role. It never changes an existing account: a
    // password changed later in the configuration is not applied, and an existing e-mail without the role is left alone
    // (it could be a real account). Requires the roles created by IdentitySeeder. Never logs e-mails or passwords.
    public sealed class IdentityDevelopmentAccountSeeder
    {
        private readonly RoleManager<IdentityRole<Guid>> _roles;
        private readonly UserManager<ApplicationUser> _users;
        private readonly IdentityDevelopmentAccountsOptions _options;
        private readonly TimeProvider _time;
        private readonly ILogger<IdentityDevelopmentAccountSeeder> _logger;

        public IdentityDevelopmentAccountSeeder(
            RoleManager<IdentityRole<Guid>> roles,
            UserManager<ApplicationUser> users,
            IOptions<IdentityDevelopmentAccountsOptions> options,
            TimeProvider time,
            ILogger<IdentityDevelopmentAccountSeeder> logger)
        {
            _roles = roles;
            _users = users;
            _options = options.Value;
            _time = time;
            _logger = logger;
        }

        public async Task SeedAsync(CancellationToken cancellationToken = default)
        {
            var accounts = Validate();
            foreach (var (email, role) in accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await _roles.RoleExistsAsync(role))
                {
                    throw new InvalidOperationException(
                        $"Development accounts: role '{role}' does not exist. Enable IdentityBootstrap so roles are created.");
                }

                await EnsureAccountAsync(email, role);
            }
        }

        private List<(string Email, string Role)> Validate()
        {
            if (string.IsNullOrEmpty(_options.Password))
            {
                throw new InvalidOperationException("Development accounts: IdentityDevelopmentAccounts:Password is not configured.");
            }

            var accounts = new List<(string Email, string Role)>();
            foreach (var entry in _options.Accounts)
            {
                var email = entry.Email.Trim();
                var role = SecurityRoles.All.FirstOrDefault(r => string.Equals(r, entry.Role.Trim(), StringComparison.OrdinalIgnoreCase));
                if (email.Length == 0)
                {
                    throw new InvalidOperationException("Development accounts: every account needs an e-mail.");
                }

                if (role is null || role == SecurityRoles.Administrator)
                {
                    throw new InvalidOperationException(
                        $"Development accounts: role '{entry.Role}' is not allowed (valid: any role except {SecurityRoles.Administrator}).");
                }

                if (accounts.Any(a => string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("Development accounts: an e-mail is listed more than once.");
                }

                accounts.Add((email, role));
            }

            return accounts;
        }

        private async Task EnsureAccountAsync(string email, string role)
        {
            var user = await _users.FindByEmailAsync(email);
            if (user is null)
            {
                user = ApplicationUser.Create(email, email, _time.GetUtcNow());
                EnsureSucceeded(await _users.CreateAsync(user, _options.Password!));
                EnsureSucceeded(await _users.AddToRoleAsync(user, role));
                _logger.LogInformation("Development account {UserId} created with role {Role}.", user.Id, role);
                return;
            }

            if (!await _users.IsInRoleAsync(user, role))
            {
                _logger.LogWarning("Development account {UserId} already exists without role {Role}; it was not changed.", user.Id, role);
            }
        }

        // Error codes only: Identity descriptions could echo configuration values.
        private static void EnsureSucceeded(IdentityResult result)
        {
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Development account seeding failed: {string.Join(", ", result.Errors.Select(error => error.Code))}.");
            }
        }
    }
}
