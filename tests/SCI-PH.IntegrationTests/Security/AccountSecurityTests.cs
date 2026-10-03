using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Persistence.Contexts;
using SCI_PH.Persistence.Identity;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.IntegrationTests.Security
{
    public class AccountSecurityTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        [Fact]
        public async Task IdentityPolicyIsApplied()
        {
            await using var context = new SecurityTestContext();
            var options = await context.RunAsync(sp => Task.FromResult(sp.GetRequiredService<IOptions<IdentityOptions>>().Value));

            Assert.True(options.User.RequireUniqueEmail);
            Assert.True(options.Password.RequiredLength >= 10);
            Assert.True(options.Password is { RequireDigit: true, RequireLowercase: true, RequireUppercase: true, RequireNonAlphanumeric: true });
            Assert.Equal(IdentityPolicy.MaxFailedAccessAttempts, options.Lockout.MaxFailedAccessAttempts);
            Assert.Equal(TimeSpan.FromMinutes(15), options.Lockout.DefaultLockoutTimeSpan);
            Assert.True(options.Lockout.AllowedForNewUsers);
        }

        [Theory]
        [InlineData("short1!A")]
        [InlineData("alllowercase1!")]
        [InlineData("NoDigitsHere!!")]
        [InlineData("NoSymbols12345")]
        public async Task WeakPasswordsAreRejected(string weakPassword)
        {
            await using var context = new SecurityTestContext();

            var result = await context.RunAsync(sp => sp.GetRequiredService<UserManager<ApplicationUser>>()
                .CreateAsync(ApplicationUser.Create("weak", "weak@sci-ph.test", DateTimeOffset.UtcNow), weakPassword));

            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task DuplicateEmailIsRejected()
        {
            await using var context = new SecurityTestContext();
            var existing = await context.CreateUserAsync(SecurityRoles.Doctor);

            var result = await context.RunAsync(sp => sp.GetRequiredService<UserManager<ApplicationUser>>()
                .CreateAsync(ApplicationUser.Create("another-name", existing.Email.ToUpperInvariant(), DateTimeOffset.UtcNow), TestSettings.NewPassword()));

            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task NewAccountIsActive()
        {
            var user = ApplicationUser.Create("someone", "someone@sci-ph.test", DateTimeOffset.UtcNow);

            Assert.True(user.IsActive);
            Assert.Null(user.DeactivatedAtUtc);
            await Task.CompletedTask;
        }

        [Fact]
        public async Task ChangePasswordWithWrongCurrentPasswordKeepsSessions()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);
            var login = (await context.LoginAsync(user.Email, user.Password))!;

            var result = await context.AuthenticationAsync(auth => auth.ChangePasswordAsync(
                user.Id, new ChangePasswordRequest(TestSettings.NewPassword(), TestSettings.NewPassword()), ClientContext.Unknown, None));

            Assert.False(result.Succeeded);
            Assert.Equal(SecurityFailureReason.PasswordMismatch, result.FailureReason);
            Assert.Empty(result.Errors);
            Assert.NotNull(await context.RefreshAsync(login.RefreshToken));
        }

        [Fact]
        public async Task ChangePasswordRejectsAWeakNewPassword()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);

            var result = await context.AuthenticationAsync(auth => auth.ChangePasswordAsync(
                user.Id, new ChangePasswordRequest(user.Password, "weak"), ClientContext.Unknown, None));

            Assert.Equal(SecurityFailureReason.PasswordPolicy, result.FailureReason);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        public async Task ChangePasswordRevokesSessionsAndRenewsTheSecurityStamp()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Nurse);
            var login = (await context.LoginAsync(user.Email, user.Password))!;
            var stampBefore = await SecurityStampAsync(context, user.Id);
            var newPassword = TestSettings.NewPassword();

            var result = await context.AuthenticationAsync(auth => auth.ChangePasswordAsync(
                user.Id, new ChangePasswordRequest(user.Password, newPassword), ClientContext.Unknown, None));

            Assert.True(result.Succeeded);
            Assert.NotEqual(stampBefore, await SecurityStampAsync(context, user.Id));
            Assert.Null(await context.RefreshAsync(login.RefreshToken));
            Assert.Null(await context.LoginAsync(user.Email, user.Password));
            Assert.NotNull(await context.LoginAsync(user.Email, newPassword));
        }

        [Fact]
        public async Task DeactivationKeepsTheAccountAndEndsSessions()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Paramedic);
            await context.LoginAsync(user.Email, user.Password);

            Assert.Equal(UserAdministrationOutcome.Succeeded, await context.AccountsAsync(accounts => accounts.DeactivateAsync(user.Id, Guid.NewGuid(), ClientContext.Unknown, None)));

            var account = await context.DbAsync(db => db.Users.SingleAsync(u => u.Id == user.Id));
            Assert.False(account.IsActive);
            Assert.NotNull(account.DeactivatedAtUtc);
            Assert.False(await context.DbAsync(db => db.RefreshTokens.AnyAsync(t => t.UserId == user.Id && t.RevokedAtUtc == null)));
            Assert.Equal(UserAdministrationOutcome.NotFound, await context.AccountsAsync(accounts => accounts.DeactivateAsync(Guid.NewGuid(), Guid.NewGuid(), ClientContext.Unknown, None)));
        }

        [Fact]
        public async Task SecurityAuditIsAppendOnlyAndHoldsNoSecrets()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);
            var login = (await context.LoginAsync(user.Email, user.Password))!;
            await context.LoginAsync(user.Email, TestSettings.NewPassword());

            await context.RunAsync(async sp =>
            {
                var db = sp.GetRequiredService<IdentityDbContext>();
                var events = await db.SecurityAuditEvents.ToListAsync();
                var text = string.Join("|", events.Select(e => $"{e.IpAddress}{e.UserAgent}{e.FailureReason}"));
                Assert.DoesNotContain(user.Password, text);
                Assert.DoesNotContain(login.RefreshToken, text);
                Assert.DoesNotContain(login.AccessToken, text);

                db.SecurityAuditEvents.Remove(events[0]);
                await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            });
        }

        private static Task<string?> SecurityStampAsync(SecurityTestContext context, Guid userId) =>
            context.DbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.SecurityStamp).SingleAsync());
    }
}
