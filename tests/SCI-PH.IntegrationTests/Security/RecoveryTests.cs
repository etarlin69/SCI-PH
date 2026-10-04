using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Accounts;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Persistence.Identity;

namespace SCI_PH.IntegrationTests.Security
{
    public class RecoveryTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        [Fact]
        public async Task UnknownEmailsGetNoMessageAndNoError()
        {
            await using var context = new SecurityTestContext();

            await Recovery(context, s => s.RequestPasswordResetAsync("nobody@sci-ph.test", ClientContext.Unknown, None));
            await Recovery(context, s => s.RequestEmailConfirmationAsync("nobody@sci-ph.test", ClientContext.Unknown, None));

            Assert.Empty(context.Email.Sent);
        }

        [Fact]
        public async Task PasswordResetChangesThePasswordRevokesSessionsAndLiftsLockout()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);
            var session = (await context.LoginAsync(user.Email, user.Password))!;
            for (var attempt = 0; attempt < IdentityPolicy.MaxFailedAccessAttempts; attempt++)
            {
                await context.LoginAsync(user.Email, TestSettings.NewPassword());
            }

            await Recovery(context, s => s.RequestPasswordResetAsync(user.Email, ClientContext.Unknown, None));
            var message = context.Email.LastTo(user.Email);
            Assert.StartsWith(TestSettings.ResetPasswordUrl, message.TextBody.Split('\n').First(l => l.StartsWith("http", StringComparison.Ordinal)));
            var newPassword = TestSettings.NewPassword();
            var result = await Recovery(context, s => s.ResetPasswordAsync(new ResetPasswordRequest(
                Guid.Parse(FakeEmailSender.LinkParameter(message, "userId")), FakeEmailSender.LinkParameter(message, "token"), newPassword),
                ClientContext.Unknown, None));

            Assert.True(result.Succeeded);
            Assert.Null(await context.RefreshAsync(session.RefreshToken));
            Assert.Null(await context.LoginAsync(user.Email, user.Password));
            Assert.NotNull(await context.LoginAsync(user.Email, newPassword));
            Assert.True(await context.DbAsync(db => db.SecurityAuditEvents.AnyAsync(e => e.EventType == SecurityAuditEventType.PasswordResetSucceeded)));
            Assert.DoesNotContain(user.Password, message.TextBody);
        }

        [Fact]
        public async Task ResetRejectsBadTokensAndWeakPasswords()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Nurse);
            await Recovery(context, s => s.RequestPasswordResetAsync(user.Email, ClientContext.Unknown, None));
            var token = FakeEmailSender.LinkParameter(context.Email.LastTo(user.Email), "token");

            var forged = await Recovery(context, s => s.ResetPasswordAsync(new ResetPasswordRequest(user.Id, "bm90LWEtdG9rZW4", TestSettings.NewPassword()), ClientContext.Unknown, None));
            var malformed = await Recovery(context, s => s.ResetPasswordAsync(new ResetPasswordRequest(user.Id, "%%%", TestSettings.NewPassword()), ClientContext.Unknown, None));
            var weak = await Recovery(context, s => s.ResetPasswordAsync(new ResetPasswordRequest(user.Id, token, "weak"), ClientContext.Unknown, None));

            Assert.Equal(SecurityFailureReason.InvalidToken, forged.FailureReason);
            Assert.Equal(SecurityFailureReason.InvalidToken, malformed.FailureReason);
            Assert.Equal(SecurityFailureReason.PasswordPolicy, weak.FailureReason);
            Assert.NotEmpty(weak.Errors);
        }

        [Fact]
        public async Task EmailConfirmationFlow()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);

            await Recovery(context, s => s.RequestEmailConfirmationAsync(user.Email, ClientContext.Unknown, None));
            var message = context.Email.LastTo(user.Email);
            var bad = await Recovery(context, s => s.ConfirmEmailAsync(new ConfirmEmailRequest(user.Id, "bm90LWEtdG9rZW4"), ClientContext.Unknown, None));
            var confirmed = await Recovery(context, s => s.ConfirmEmailAsync(
                new ConfirmEmailRequest(user.Id, FakeEmailSender.LinkParameter(message, "token")), ClientContext.Unknown, None));
            var sentBefore = context.Email.Sent.Count;
            await Recovery(context, s => s.RequestEmailConfirmationAsync(user.Email, ClientContext.Unknown, None));

            Assert.False(bad);
            Assert.True(confirmed);
            Assert.True(await context.DbAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.EmailConfirmed).SingleAsync()));
            Assert.Equal(sentBefore, context.Email.Sent.Count);
        }

        [Fact]
        public async Task DeliveryFailureIsAuditedButNotSurfaced()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);
            context.Email.Fail = true;

            await Recovery(context, s => s.RequestPasswordResetAsync(user.Email, ClientContext.Unknown, None));

            Assert.True(await context.DbAsync(db => db.SecurityAuditEvents.AnyAsync(e => e.FailureReason == SecurityFailureReason.EmailDeliveryFailed)));
        }

        private static Task<T> Recovery<T>(SecurityTestContext context, Func<AccountRecoveryService, Task<T>> action) => context.With(action);

        private static Task Recovery(SecurityTestContext context, Func<AccountRecoveryService, Task> action) => context.With(action);
    }
}
