using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Security
{
    public class UserAdministrationTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        [Fact]
        public async Task RoleChangeReplacesRolesEndsSessionsAndIsAudited()
        {
            await using var context = new SecurityTestContext();
            var admin = await context.CreateUserAsync(SecurityRoles.Administrator);
            var user = await context.CreateUserAsync(SecurityRoles.Nurse);
            var session = (await context.LoginAsync(user.Email, user.Password))!;

            var outcome = await context.AccountsAsync(a => a.SetRolesAsync(user.Id, [SecurityRoles.Doctor], admin.Id, ClientContext.Unknown, None));

            Assert.Equal(UserAdministrationOutcome.Succeeded, outcome);
            Assert.Null(await context.RefreshAsync(session.RefreshToken));
            Assert.Equal([SecurityRoles.Doctor], (await context.LoginAsync(user.Email, user.Password))!.User.Roles);
            Assert.True(await context.DbAsync(db => db.SecurityAuditEvents.AnyAsync(e =>
                e.EventType == SecurityAuditEventType.RoleChanged && e.UserId == user.Id && e.ActorUserId == admin.Id)));
        }

        [Fact]
        public async Task OnlyPredefinedRolesAndNoPatientMixing()
        {
            await using var context = new SecurityTestContext();
            var admin = await context.CreateUserAsync(SecurityRoles.Administrator);
            var user = await context.CreateUserAsync(SecurityRoles.Nurse);

            Assert.Equal(UserAdministrationOutcome.InvalidRoles,
                await context.AccountsAsync(a => a.SetRolesAsync(user.Id, ["SuperUser"], admin.Id, ClientContext.Unknown, None)));
            Assert.Equal(UserAdministrationOutcome.InvalidRoles,
                await context.AccountsAsync(a => a.SetRolesAsync(user.Id, [SecurityRoles.Patient, SecurityRoles.Doctor], admin.Id, ClientContext.Unknown, None)));
            Assert.Equal(UserAdministrationOutcome.NotFound,
                await context.AccountsAsync(a => a.SetRolesAsync(Guid.NewGuid(), [SecurityRoles.Doctor], admin.Id, ClientContext.Unknown, None)));
        }

        [Fact]
        public async Task TheLastAdministratorAndSelfDemotionAreProtected()
        {
            await using var context = new SecurityTestContext();
            var admin = await context.CreateUserAsync(SecurityRoles.Administrator);
            var operatorAdmin = Guid.NewGuid();

            Assert.Equal(UserAdministrationOutcome.LastAdministrator,
                await context.AccountsAsync(a => a.SetRolesAsync(admin.Id, [SecurityRoles.Doctor], operatorAdmin, ClientContext.Unknown, None)));
            Assert.Equal(UserAdministrationOutcome.LastAdministrator,
                await context.AccountsAsync(a => a.DeactivateAsync(admin.Id, operatorAdmin, ClientContext.Unknown, None)));

            var second = await context.CreateUserAsync(SecurityRoles.Administrator);
            Assert.Equal(UserAdministrationOutcome.SelfModification,
                await context.AccountsAsync(a => a.SetRolesAsync(second.Id, [SecurityRoles.Doctor], second.Id, ClientContext.Unknown, None)));
            Assert.Equal(UserAdministrationOutcome.SelfModification,
                await context.AccountsAsync(a => a.DeactivateAsync(second.Id, second.Id, ClientContext.Unknown, None)));
            Assert.Equal(UserAdministrationOutcome.Succeeded,
                await context.AccountsAsync(a => a.SetRolesAsync(admin.Id, [SecurityRoles.Doctor], second.Id, ClientContext.Unknown, None)));
        }

        [Fact]
        public async Task ListingIsPagedAndSearchable()
        {
            await using var context = new SecurityTestContext();
            var users = new List<TestAccount>();
            for (var i = 0; i < 3; i++)
            {
                users.Add(await context.CreateUserAsync(SecurityRoles.Nurse));
            }

            var page = await context.AccountsAsync(a => a.ListAsync(null, 1, 2, None));
            var search = await context.AccountsAsync(a => a.ListAsync(users[1].Email, 1, 20, None));

            Assert.Equal(3, page.Total);
            Assert.Equal(2, page.Items.Count);
            var match = Assert.Single(search.Items);
            Assert.Equal(users[1].Id, match.Id);
            Assert.Equal([SecurityRoles.Nurse], match.Roles);
        }

        [Fact]
        public async Task AdministrativeSessionRevocationEndsRefreshTokens()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);
            var session = (await context.LoginAsync(user.Email, user.Password))!;

            Assert.Equal(UserAdministrationOutcome.Succeeded,
                await context.AccountsAsync(a => a.RevokeSessionsAsync(user.Id, Guid.NewGuid(), ClientContext.Unknown, None)));
            Assert.Null(await context.RefreshAsync(session.RefreshToken));
        }
    }
}
