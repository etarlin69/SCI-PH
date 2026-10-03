using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Authentication;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Security
{
    // Each MFA challenge produces at most one session, whatever valid codes are presented with it.
    public class MfaChallengeReplayTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        [Fact]
        public async Task ChallengeWithCorrectCodeStartsASessionAndIsConsumed()
        {
            await using var context = new SecurityTestContext();
            var (user, key, _) = await UserWithMfaAsync(context);
            var challenge = await ChallengeAsync(context, user);

            var session = await Verify(context, challenge, code: Totp.Code(key));

            Assert.NotNull(session);
            Assert.NotNull(await context.RefreshAsync(session.RefreshToken));
            var stored = await context.DbAsync(db => db.MfaLoginChallenges.SingleAsync(c => c.UserId == user.Id));
            Assert.NotNull(stored.ConsumedAtUtc);
            Assert.Equal(await HashAsync(context, challenge), stored.ChallengeHash);
            Assert.NotEqual(new JsonWebToken(challenge).Id, stored.ChallengeHash);
        }

        [Fact]
        public async Task ReusingTheChallengeWithTheSameCodeIsRejected()
        {
            await using var context = new SecurityTestContext();
            var (user, key, _) = await UserWithMfaAsync(context);
            var challenge = await ChallengeAsync(context, user);
            var code = Totp.Code(key);

            Assert.NotNull(await Verify(context, challenge, code: code));
            Assert.Null(await Verify(context, challenge, code: code));
            Assert.Equal(1, await ActiveRefreshTokensAsync(context, user.Id));
        }

        [Fact]
        public async Task ReusingTheChallengeWithAnotherValidCodeIsRejected()
        {
            await using var context = new SecurityTestContext();
            var (user, key, _) = await UserWithMfaAsync(context);
            var challenge = await ChallengeAsync(context, user);

            Assert.NotNull(await Verify(context, challenge, code: Totp.Code(key)));
            Assert.Null(await Verify(context, challenge, code: Totp.Code(key, stepOffset: -1)));
            Assert.Null(await Verify(context, challenge, code: Totp.Code(key, stepOffset: 1)));
            Assert.Equal(1, await ActiveRefreshTokensAsync(context, user.Id));
        }

        [Fact]
        public async Task ExpiredChallengeIsRejected()
        {
            await using var context = new SecurityTestContext();
            var (user, key, _) = await UserWithMfaAsync(context);
            var challenge = await ChallengeAsync(context, user);

            context.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

            Assert.Null(await Verify(context, challenge, code: Totp.Code(key)));
            Assert.Null((await context.DbAsync(db => db.MfaLoginChallenges.SingleAsync(c => c.UserId == user.Id))).ConsumedAtUtc);
        }

        [Fact]
        public async Task ValidlySignedChallengeThatWasNeverIssuedIsRejected()
        {
            await using var context = new SecurityTestContext();
            var (user, key, _) = await UserWithMfaAsync(context);
            var forged = context.With<ITokenService, MfaChallenge>(tokens =>
                Task.FromResult(tokens.CreateMfaChallenge(user.Id, Guid.NewGuid().ToString("N"))));

            Assert.Null(await Verify(context, (await forged).Token, code: Totp.Code(key)));
            Assert.Equal(0, await ActiveRefreshTokensAsync(context, user.Id));
        }

        [Fact]
        public async Task ConsumedChallengeIsRejected()
        {
            await using var context = new SecurityTestContext();
            var (user, key, _) = await UserWithMfaAsync(context);
            var challenge = await ChallengeAsync(context, user);
            Assert.NotNull(await Verify(context, challenge, code: Totp.Code(key)));

            Assert.NotNull((await context.DbAsync(db => db.MfaLoginChallenges.SingleAsync(c => c.UserId == user.Id))).ConsumedAtUtc);
            Assert.Null(await Verify(context, challenge, code: Totp.Code(key)));
            Assert.True(await context.DbAsync(db => db.SecurityAuditEvents.AnyAsync(e =>
                e.EventType == SecurityAuditEventType.MfaFailed && e.UserId == user.Id && e.FailureReason == SecurityFailureReason.InvalidToken)));
        }

        [Fact]
        public async Task ChallengeOfOneUserCannotLogInAnotherUser()
        {
            await using var context = new SecurityTestContext();
            var (userA, _, _) = await UserWithMfaAsync(context);
            var (userB, keyB, _) = await UserWithMfaAsync(context);
            var challengeA = await ChallengeAsync(context, userA);

            // Same (valid, unconsumed) challenge id re-signed for user B: the persisted owner does not match.
            var retargeted = await context.With<ITokenService, MfaChallenge>(tokens =>
                Task.FromResult(tokens.CreateMfaChallenge(userB.Id, new JsonWebToken(challengeA).Id)));

            Assert.Null(await Verify(context, retargeted.Token, code: Totp.Code(keyB)));
            Assert.Null(await Verify(context, challengeA, code: Totp.Code(keyB)));
            Assert.Equal(0, await ActiveRefreshTokensAsync(context, userB.Id));
        }

        [Fact]
        public async Task RecoveryCodeConsumesTheChallengeAndCannotBeFollowedByAReplay()
        {
            await using var context = new SecurityTestContext();
            var (user, key, codes) = await UserWithMfaAsync(context);
            var challenge = await ChallengeAsync(context, user);

            Assert.NotNull(await Verify(context, challenge, recoveryCode: codes[0]));
            Assert.Null(await Verify(context, challenge, recoveryCode: codes[1]));
            Assert.Null(await Verify(context, challenge, code: Totp.Code(key)));
            Assert.Equal(1, await ActiveRefreshTokensAsync(context, user.Id));
            Assert.NotNull(await Verify(context, await ChallengeAsync(context, user), recoveryCode: codes[1]));
        }

        [Fact]
        public async Task WrongCodeDoesNotConsumeTheChallenge()
        {
            await using var context = new SecurityTestContext();
            var (user, key, _) = await UserWithMfaAsync(context);
            var challenge = await ChallengeAsync(context, user);

            Assert.Null(await Verify(context, challenge, code: "000000"));
            Assert.Null(await Verify(context, challenge, recoveryCode: "not-a-recovery-code"));
            Assert.Null((await context.DbAsync(db => db.MfaLoginChallenges.SingleAsync(c => c.UserId == user.Id))).ConsumedAtUtc);
            Assert.Equal(2, await context.DbAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.AccessFailedCount).SingleAsync()));
            Assert.NotNull(await Verify(context, challenge, code: Totp.Code(key)));
        }

        [Fact]
        public async Task ANewLoginInvalidatesThePreviousChallenge()
        {
            await using var context = new SecurityTestContext();
            var (user, key, _) = await UserWithMfaAsync(context);
            var first = await ChallengeAsync(context, user);
            var second = await ChallengeAsync(context, user);

            Assert.Null(await Verify(context, first, code: Totp.Code(key)));
            Assert.NotNull(await Verify(context, second, code: Totp.Code(key)));
            Assert.Equal(1, await context.DbAsync(db => db.MfaLoginChallenges.CountAsync(c => c.UserId == user.Id)));
        }

        internal static async Task<string> ChallengeAsync(SecurityTestContext context, TestAccount user) =>
            (await context.LoginRawAsync(user.Email, user.Password))!.Challenge!.Token;

        internal static Task<AuthenticationResult?> Verify(SecurityTestContext context, string challenge, string? code = null, string? recoveryCode = null) =>
            context.AuthenticationAsync(auth => auth.VerifyMfaAsync(new MfaVerifyRequest(challenge, code, recoveryCode), ClientContext.Unknown, None));

        internal static Task<int> ActiveRefreshTokensAsync(SecurityTestContext context, Guid userId) =>
            context.DbAsync(db => db.RefreshTokens.CountAsync(t => t.UserId == userId && t.RevokedAtUtc == null));

        internal static async Task<(TestAccount User, string Key, IReadOnlyList<string> Codes)> UserWithMfaAsync(SecurityTestContext context)
        {
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);
            var setup = (await context.With<MfaManagementService, MfaSetup?>(s => s.BeginSetupAsync(user.Id, ClientContext.Unknown, None)))!;
            var codes = (await context.With<MfaManagementService, RecoveryCodes?>(s =>
                s.EnableAsync(user.Id, Totp.Code(setup.SharedKey), ClientContext.Unknown, None)))!;
            return (user, setup.SharedKey, codes.Codes);
        }

        private static Task<string> HashAsync(SecurityTestContext context, string challenge) =>
            context.With<ISecureTokenGenerator, string>(secrets => Task.FromResult(secrets.Hash(new JsonWebToken(challenge).Id)));
    }
}
