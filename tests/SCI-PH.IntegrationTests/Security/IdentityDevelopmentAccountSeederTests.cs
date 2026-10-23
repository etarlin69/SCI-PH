using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SCI_PH.Application.Common.Security;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Persistence;
using SCI_PH.Persistence.Identity.Models;
using SCI_PH.Persistence.Identity.Seed;

namespace SCI_PH.IntegrationTests.Security
{
    public class IdentityDevelopmentAccountSeederTests
    {
        private static readonly string[] _roles =
            [SecurityRoles.Doctor, SecurityRoles.Nurse, SecurityRoles.Paramedic, SecurityRoles.Receptionist, SecurityRoles.Patient];

        [Fact]
        public async Task OneAccountPerRoleIsCreatedOnceAndCanSignIn()
        {
            var password = TestSettings.NewPassword();
            await using var context = new SecurityTestContext(Settings(password, _roles));
            await context.SeedAsync();

            await SeedDevelopmentAccountsAsync(context);
            await SeedDevelopmentAccountsAsync(context);

            Assert.Equal(_roles.Length, await context.DbAsync(db => db.Users.CountAsync()));
            foreach (var role in _roles)
            {
                var login = await context.LoginAsync(EmailFor(role), password);
                Assert.NotNull(login);
                Assert.Equal([role], login.User.Roles);
                Assert.Equal(RolePermissions.For(role).Order(StringComparer.Ordinal), login.User.Permissions);
            }

            // The mobile paramedic area is granted by the permission, which the Paramedic account receives through its role.
            var paramedic = await context.LoginAsync(EmailFor(SecurityRoles.Paramedic), password);
            Assert.Contains(Permissions.Emergencies.Operate, paramedic!.User.Permissions);
            Assert.DoesNotContain(password, context.Logs.Text);
        }

        [Theory]
        [InlineData(SecurityRoles.Administrator)]
        [InlineData("SuperUser")]
        public async Task AdministratorAndUnknownRolesAreRejected(string role)
        {
            await using var context = new SecurityTestContext(Settings(TestSettings.NewPassword(), role));
            await context.SeedAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => SeedDevelopmentAccountsAsync(context));

            Assert.Equal(0, await context.DbAsync(db => db.Users.CountAsync()));
        }

        [Fact]
        public async Task MissingPasswordIsRejected()
        {
            await using var context = new SecurityTestContext(Settings(password: null, SecurityRoles.Nurse));
            await context.SeedAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => SeedDevelopmentAccountsAsync(context));
        }

        [Fact]
        public async Task PasswordOutsideThePolicyFailsWithoutLoggingIt()
        {
            const string weak = "REPLACE_ME";
            await using var context = new SecurityTestContext(Settings(weak, SecurityRoles.Nurse));
            await context.SeedAsync();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SeedDevelopmentAccountsAsync(context));

            Assert.DoesNotContain(weak, error.Message);
            Assert.DoesNotContain(weak, context.Logs.Text);
            Assert.Equal(0, await context.DbAsync(db => db.Users.CountAsync()));
        }

        [Fact]
        public async Task RolesMustExistFirst()
        {
            await using var context = new SecurityTestContext(Settings(TestSettings.NewPassword(), SecurityRoles.Nurse));

            await Assert.ThrowsAsync<InvalidOperationException>(() => SeedDevelopmentAccountsAsync(context));
        }

        // An existing e-mail could be a real account: it is never given a role or a new password.
        [Fact]
        public async Task AnExistingAccountIsNotChanged()
        {
            var password = TestSettings.NewPassword();
            await using var context = new SecurityTestContext(Settings(TestSettings.NewPassword(), SecurityRoles.Paramedic));
            await context.SeedAsync();
            await context.With<UserManager<ApplicationUser>>(async users =>
            {
                var user = ApplicationUser.Create("existing", EmailFor(SecurityRoles.Paramedic), DateTimeOffset.UtcNow);
                Assert.True((await users.CreateAsync(user, password)).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, SecurityRoles.Nurse)).Succeeded);
            });

            await SeedDevelopmentAccountsAsync(context);

            var login = await context.LoginAsync(EmailFor(SecurityRoles.Paramedic), password);
            Assert.NotNull(login);
            Assert.Equal([SecurityRoles.Nurse], login.User.Roles);
        }

        [Fact]
        public async Task TheAccountsAreOnlySeededInDevelopment()
        {
            await using var context = new SecurityTestContext(Settings(TestSettings.NewPassword(), SecurityRoles.Nurse));
            await context.SeedAsync();

            foreach (var name in new[] { Environments.Production, Environments.Staging, "Testing" })
            {
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => context.RunAsync(sp => sp.RunIdentityDevelopmentAccountsAsync(new TestEnvironment(name))));
            }

            Assert.Equal(0, await context.DbAsync(db => db.Users.CountAsync()));
            await context.RunAsync(sp => sp.RunIdentityDevelopmentAccountsAsync(new TestEnvironment(Environments.Development)));
            Assert.Equal(1, await context.DbAsync(db => db.Users.CountAsync()));
        }

        [Fact]
        public async Task DisabledDoesNothingInAnyEnvironment()
        {
            var settings = Settings(TestSettings.NewPassword(), SecurityRoles.Nurse);
            settings["IdentityDevelopmentAccounts:Enabled"] = "false";
            await using var context = new SecurityTestContext(settings);
            await context.SeedAsync();

            await context.RunAsync(sp => sp.RunIdentityDevelopmentAccountsAsync(new TestEnvironment(Environments.Production)));
            await context.RunAsync(sp => sp.RunIdentityDevelopmentAccountsAsync(new TestEnvironment(Environments.Development)));

            Assert.Equal(0, await context.DbAsync(db => db.Users.CountAsync()));
        }

        private static Task SeedDevelopmentAccountsAsync(SecurityTestContext context) =>
            context.With<IdentityDevelopmentAccountSeeder>(seeder => seeder.SeedAsync());

        private static string EmailFor(string role) => $"{role.ToLowerInvariant()}@sci-ph.test";

        private static Dictionary<string, string?> Settings(string? password, params string[] roles)
        {
            var settings = new Dictionary<string, string?>
            {
                ["IdentityDevelopmentAccounts:Enabled"] = "true",
                ["IdentityDevelopmentAccounts:Password"] = password
            };
            for (var i = 0; i < roles.Length; i++)
            {
                settings[$"IdentityDevelopmentAccounts:Accounts:{i}:Email"] = EmailFor(roles[i]);
                settings[$"IdentityDevelopmentAccounts:Accounts:{i}:Role"] = roles[i];
            }

            return settings;
        }

        private sealed class TestEnvironment(string name) : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = name;
            public string ApplicationName { get; set; } = "SCI-PH.Tests";
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
