using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Authentication;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Security
{
    public class MfaTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        [Fact]
        public async Task SetupReturnsAnAuthenticatorKeyAndUri()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);

            var setup = (await Mfa(context, s => s.BeginSetupAsync(user.Id, ClientContext.Unknown, None)))!;

            Assert.False(string.IsNullOrEmpty(setup.SharedKey));
            Assert.StartsWith("otpauth://totp/", setup.AuthenticatorUri);
            Assert.Contains($"secret={setup.SharedKey}", setup.AuthenticatorUri);
            Assert.Contains("issuer=SCI-PH", setup.AuthenticatorUri);
        }

        [Fact]
        public async Task EnablingRequiresAValidCodeReturnsRecoveryCodesAndEndsSessions()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);
            var session = (await context.LoginAsync(user.Email, user.Password))!;
            var setup = (await Mfa(context, s => s.BeginSetupAsync(user.Id, ClientContext.Unknown, None)))!;

            var invalid = await Mfa(context, s => s.EnableAsync(user.Id, "000000", ClientContext.Unknown, None));
            var codes = await Mfa(context, s => s.EnableAsync(user.Id, Totp.Code(setup.SharedKey), ClientContext.Unknown, None));

            Assert.Null(invalid);
            Assert.Equal(10, codes!.Codes.Count);
            Assert.True(await context.With<IAuthenticatorService, bool>(a => a.IsEnabledAsync(user.Id, None)));
            Assert.Null(await context.RefreshAsync(session.RefreshToken));
            Assert.Null(await Mfa(context, s => s.BeginSetupAsync(user.Id, ClientContext.Unknown, None)));
        }

        [Fact]
        public async Task PasswordAloneOnlyYieldsAChallengeAndTheCodeCompletesTheLogin()
        {
            await using var context = new SecurityTestContext();
            var (user, key, _) = await UserWithMfaAsync(context);

            var login = (await context.LoginRawAsync(user.Email, user.Password))!;
            var wrong = await Verify(context, login.Challenge!.Token, code: "123456");
            var session = await Verify(context, login.Challenge.Token, code: Totp.Code(key));

            Assert.True(login.RequiresMfa);
            Assert.Null(login.Session);
            Assert.Null(wrong);
            Assert.NotNull(session);
            Assert.NotNull(await context.RefreshAsync(session.RefreshToken));
            Assert.True(await context.DbAsync(db => db.SecurityAuditEvents.AnyAsync(e => e.EventType == SecurityAuditEventType.MfaFailed)));
            Assert.True(await context.DbAsync(db => db.SecurityAuditEvents.AnyAsync(e => e.EventType == SecurityAuditEventType.MfaSucceeded)));
        }

        [Fact]
        public async Task RecoveryCodesWorkOnce()
        {
            await using var context = new SecurityTestContext();
            var (user, _, codes) = await UserWithMfaAsync(context);

            var first = await Verify(context, (await context.LoginRawAsync(user.Email, user.Password))!.Challenge!.Token, recoveryCode: codes[0]);
            var reused = await Verify(context, (await context.LoginRawAsync(user.Email, user.Password))!.Challenge!.Token, recoveryCode: codes[0]);

            Assert.NotNull(first);
            Assert.Null(reused);
            Assert.True(await context.DbAsync(db => db.SecurityAuditEvents.AnyAsync(e => e.EventType == SecurityAuditEventType.RecoveryCodeUsed)));
        }

        [Fact]
        public async Task ChallengeCannotBeUsedAsAccessTokenNorTheOtherWayRoundAndExpires()
        {
            await using var context = new SecurityTestContext();
            var (user, key, _) = await UserWithMfaAsync(context);
            var plain = await context.CreateUserAsync(SecurityRoles.Nurse);
            var accessToken = (await context.LoginAsync(plain.Email, plain.Password))!.AccessToken;
            var challenge = (await context.LoginRawAsync(user.Email, user.Password))!.Challenge!;

            Assert.Null(await context.With<ITokenService, MfaChallengeClaims?>(tokens => tokens.ReadMfaChallengeAsync(accessToken)));
            Assert.Equal(user.Id, (await context.With<ITokenService, MfaChallengeClaims?>(tokens => tokens.ReadMfaChallengeAsync(challenge.Token)))!.UserId);
            Assert.Null(await Verify(context, accessToken, code: Totp.Code(key)));

            context.Time.Advance(TimeSpan.FromMinutes(6));
            Assert.Null(await Verify(context, challenge.Token, code: Totp.Code(key)));
        }

        [Fact]
        public async Task DisableAndRegenerateRequireAValidCode()
        {
            await using var context = new SecurityTestContext();
            var (user, key, codes) = await UserWithMfaAsync(context);

            Assert.Null(await Mfa(context, s => s.RegenerateRecoveryCodesAsync(user.Id, "000000", ClientContext.Unknown, None)));
            var regenerated = (await Mfa(context, s => s.RegenerateRecoveryCodesAsync(user.Id, Totp.Code(key), ClientContext.Unknown, None)))!;
            Assert.Null(await Verify(context, (await context.LoginRawAsync(user.Email, user.Password))!.Challenge!.Token, recoveryCode: codes[1]));
            Assert.NotNull(await Verify(context, (await context.LoginRawAsync(user.Email, user.Password))!.Challenge!.Token, recoveryCode: regenerated.Codes[0]));

            Assert.False(await Mfa(context, s => s.DisableAsync(user.Id, "000000", ClientContext.Unknown, None)));
            Assert.True(await Mfa(context, s => s.DisableAsync(user.Id, Totp.Code(key), ClientContext.Unknown, None)));
            Assert.False((await context.LoginRawAsync(user.Email, user.Password))!.RequiresMfa);
        }

        [Fact]
        public async Task FailedSecondFactorsLockTheAccount()
        {
            await using var context = new SecurityTestContext();
            var (user, key, _) = await UserWithMfaAsync(context);
            var challenge = (await context.LoginRawAsync(user.Email, user.Password))!.Challenge!.Token;

            for (var attempt = 0; attempt < SCI_PH.Persistence.Identity.IdentityPolicy.MaxFailedAccessAttempts; attempt++)
            {
                await Verify(context, challenge, code: "000000");
            }

            Assert.Null(await Verify(context, challenge, code: Totp.Code(key)));
        }

        private static Task<T> Mfa<T>(SecurityTestContext context, Func<MfaManagementService, Task<T>> action) => context.With(action);

        private static Task<AuthenticationResult?> Verify(SecurityTestContext context, string challenge, string? code = null, string? recoveryCode = null) =>
            context.AuthenticationAsync(auth => auth.VerifyMfaAsync(new MfaVerifyRequest(challenge, code, recoveryCode), ClientContext.Unknown, None));

        private static async Task<(TestAccount User, string Key, IReadOnlyList<string> Codes)> UserWithMfaAsync(SecurityTestContext context)
        {
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);
            var setup = (await Mfa(context, s => s.BeginSetupAsync(user.Id, ClientContext.Unknown, None)))!;
            var codes = (await Mfa(context, s => s.EnableAsync(user.Id, Totp.Code(setup.SharedKey), ClientContext.Unknown, None)))!;
            return (user, setup.SharedKey, codes.Codes);
        }
    }
}
