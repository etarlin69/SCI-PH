using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Common.Security;
using SCI_PH.Persistence.Identity;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Security
{
    public class LoginTests
    {
        [Fact]
        public async Task ValidCredentialsReturnTokensRolesAndPermissions()
        {
            await using var context = new SecurityTestContext();
            var nurse = await context.CreateUserAsync(SecurityRoles.Nurse);

            var result = await context.LoginAsync(nurse.Email, nurse.Password);

            Assert.NotNull(result);
            Assert.False(string.IsNullOrEmpty(result.AccessToken));
            Assert.False(string.IsNullOrEmpty(result.RefreshToken));
            Assert.Equal(context.Time.GetUtcNow().AddMinutes(TestSettings.AccessTokenLifetimeMinutes), result.AccessTokenExpiresAtUtc);
            Assert.Equal(nurse.Id, result.User.Id);
            Assert.Equal([SecurityRoles.Nurse], result.User.Roles);
            Assert.Equal(RolePermissions.For(SecurityRoles.Nurse).Order(StringComparer.Ordinal), result.User.Permissions);
        }

        [Fact]
        public async Task UserNameAlsoWorksAndLastLoginIsRecorded()
        {
            await using var context = new SecurityTestContext();
            var doctor = await context.CreateUserAsync(SecurityRoles.Doctor);

            Assert.NotNull(await context.LoginAsync(doctor.UserName, doctor.Password));

            var lastLogin = await context.DbAsync(db => db.Users.Where(u => u.Id == doctor.Id).Select(u => u.LastLoginAtUtc).SingleAsync());
            Assert.Equal(context.Time.GetUtcNow(), lastLogin);
        }

        [Fact]
        public async Task WrongPasswordAndUnknownAccountFailTheSameWayButAuditDiffers()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);

            Assert.Null(await context.LoginAsync(user.Email, TestSettings.NewPassword()));
            Assert.Null(await context.LoginAsync("nobody@sci-ph.test", TestSettings.NewPassword()));

            var failures = await context.DbAsync(db => db.SecurityAuditEvents
                .Where(e => e.EventType == SecurityAuditEventType.LoginFailed)
                .Select(e => new { e.UserId, e.FailureReason })
                .ToListAsync());
            Assert.Contains(failures, f => f.UserId == user.Id && f.FailureReason == SecurityFailureReason.InvalidCredentials);
            Assert.Contains(failures, f => f.UserId == null && f.FailureReason == SecurityFailureReason.AccountNotFound);
        }

        [Fact]
        public async Task RepeatedFailuresLockTheAccountEvenForTheRightPassword()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Paramedic);

            for (var attempt = 0; attempt < IdentityPolicy.MaxFailedAccessAttempts; attempt++)
            {
                Assert.Null(await context.LoginAsync(user.Email, TestSettings.NewPassword()));
            }

            Assert.Null(await context.LoginAsync(user.Email, user.Password));
            var events = await context.DbAsync(db => db.SecurityAuditEvents.Where(e => e.UserId == user.Id).ToListAsync());
            Assert.Single(events, e => e.EventType == SecurityAuditEventType.AccountLocked);
            Assert.Contains(events, e => e.FailureReason == SecurityFailureReason.LockedOut);
        }

        [Fact]
        public async Task InactiveAccountCannotLogIn()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Receptionist);
            await context.AccountsAsync(accounts => accounts.DeactivateAsync(user.Id, Guid.NewGuid(), SCI_PH.Application.DTOs.Security.ClientContext.Unknown, CancellationToken.None));

            Assert.Null(await context.LoginAsync(user.Email, user.Password));
            Assert.True(await context.DbAsync(db => db.SecurityAuditEvents.AnyAsync(e =>
                e.UserId == user.Id && e.FailureReason == SecurityFailureReason.AccountInactive)));
        }

        [Fact]
        public async Task SuccessfulLoginResetsTheFailedAttemptCounter()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);

            for (var attempt = 0; attempt < IdentityPolicy.MaxFailedAccessAttempts - 1; attempt++)
            {
                await context.LoginAsync(user.Email, TestSettings.NewPassword());
            }

            Assert.NotNull(await context.LoginAsync(user.Email, user.Password));
            Assert.Equal(0, await context.DbAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.AccessFailedCount).SingleAsync()));
        }
    }
}
