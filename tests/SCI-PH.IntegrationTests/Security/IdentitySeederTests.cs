using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Application.Common.Security;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.IntegrationTests.Security
{
    public class IdentitySeederTests
    {
        [Fact]
        public async Task SeedingCreatesEveryRoleWithItsPermissions()
        {
            await using var context = new SecurityTestContext();

            await context.SeedAsync();

            var roles = await context.DbAsync(db => db.Roles.Select(r => r.Name!).ToListAsync());
            Assert.Equal(SecurityRoles.All.Order(), roles.Order());
            foreach (var role in SecurityRoles.All)
            {
                Assert.Equal(RolePermissions.For(role).Order(), (await PermissionsOfAsync(context, role)).Order());
            }
        }

        [Fact]
        public async Task SeedingTwiceChangesNothing()
        {
            await using var context = new SecurityTestContext();
            await context.SeedAsync();
            var before = await SnapshotAsync(context);

            await context.SeedAsync();

            Assert.Equal(before, await SnapshotAsync(context));
        }

        [Fact]
        public async Task ObsoletePermissionClaimsAreRemoved()
        {
            await using var context = new SecurityTestContext();
            await context.SeedAsync();
            await context.RunAsync(async sp =>
            {
                var roles = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
                var nurse = (await roles.FindByNameAsync(SecurityRoles.Nurse))!;
                await roles.AddClaimAsync(nurse, new Claim(CustomClaimTypes.Permission, Permissions.Users.Manage));
            });

            await context.SeedAsync();

            Assert.DoesNotContain(Permissions.Users.Manage, await PermissionsOfAsync(context, SecurityRoles.Nurse));
        }

        // An installation seeded before a permission existed receives it on the next seeding.
        [Fact]
        public async Task NewPermissionsReachExistingRoles()
        {
            await using var context = new SecurityTestContext();
            await context.SeedAsync();
            await context.RunAsync(async sp =>
            {
                var roles = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
                var paramedic = (await roles.FindByNameAsync(SecurityRoles.Paramedic))!;
                await roles.RemoveClaimAsync(paramedic, new Claim(CustomClaimTypes.Permission, Permissions.Emergencies.Operate));
            });
            Assert.DoesNotContain(Permissions.Emergencies.Operate, await PermissionsOfAsync(context, SecurityRoles.Paramedic));

            await context.SeedAsync();

            Assert.Contains(Permissions.Emergencies.Operate, await PermissionsOfAsync(context, SecurityRoles.Paramedic));
        }

        [Fact]
        public async Task NoAccountIsCreatedWithoutExternalBootstrapSettings()
        {
            await using var context = new SecurityTestContext();

            await context.SeedAsync();

            Assert.Equal(0, await context.DbAsync(db => db.Users.CountAsync()));
        }

        [Fact]
        public async Task BootstrapAdministratorIsCreatedOnceFromExternalSettings()
        {
            var email = $"admin-{Guid.NewGuid():N}@sci-ph.test";
            var password = TestSettings.NewPassword();
            await using var context = new SecurityTestContext(new Dictionary<string, string?>
            {
                ["IdentityBootstrap:AdminEmail"] = email,
                ["IdentityBootstrap:AdminPassword"] = password
            });

            await context.SeedAsync();
            await context.SeedAsync();

            Assert.Equal(1, await context.DbAsync(db => db.Users.CountAsync()));
            var login = await context.LoginAsync(email, password);
            Assert.NotNull(login);
            Assert.Equal([SecurityRoles.Administrator], login.User.Roles);
            Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), login.User.Permissions);
        }

        private static Task<List<string>> PermissionsOfAsync(SecurityTestContext context, string role) =>
            context.DbAsync(db => (
                    from claim in db.RoleClaims
                    join r in db.Roles on claim.RoleId equals r.Id
                    where r.Name == role && claim.ClaimType == CustomClaimTypes.Permission
                    select claim.ClaimValue!)
                .ToListAsync());

        private static Task<string> SnapshotAsync(SecurityTestContext context) =>
            context.DbAsync(async db =>
            {
                var roles = await db.Roles.OrderBy(r => r.Name).Select(r => $"{r.Id}:{r.Name}").ToListAsync();
                var claims = await db.RoleClaims.OrderBy(c => c.Id).Select(c => $"{c.Id}:{c.RoleId}:{c.ClaimValue}").ToListAsync();
                return string.Join(",", roles.Concat(claims));
            });
    }
}
