using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Security
{
    // AuthenticationService.RefreshSessionAsync over the real composition: a lost concurrent rotation (both requests read
    // the token as active) is a harmless, retryable outcome; a token presented after its rotation committed is still reuse.
    public class RefreshRotationRaceTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        [Fact]
        public async Task NormalRefreshSucceedsAndRotates()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);
            var login = (await context.LoginAsync(user.Email, user.Password))!;

            var outcome = await context.RefreshSessionAsync(login.RefreshToken);

            Assert.Equal(RefreshStatus.Succeeded, outcome.Status);
            Assert.NotNull(outcome.Session);
            Assert.NotEqual(login.RefreshToken, outcome.Session.RefreshToken);
            Assert.Equal(RefreshStatus.Succeeded, (await context.RefreshSessionAsync(outcome.Session.RefreshToken)).Status);
        }

        [Fact]
        public async Task ATrueLostRotationRaceIsADistinctOutcomeThatIssuesAndRevokesNothing()
        {
            var barrier = new RefreshReadBarrier();
            await using var context = new SecurityTestContext(refreshReadBarrier: barrier);
            var user = await context.CreateUserAsync(SecurityRoles.Nurse);
            var login = (await context.LoginAsync(user.Email, user.Password))!;

            barrier.Arm(2);
            var outcomes = await Task.WhenAll(
                Task.Run(() => context.RefreshSessionAsync(login.RefreshToken)),
                Task.Run(() => context.RefreshSessionAsync(login.RefreshToken)));

            var winner = Assert.Single(outcomes, o => o.Status == RefreshStatus.Succeeded);
            var loser = Assert.Single(outcomes, o => o.Status == RefreshStatus.LostConcurrencyRace);
            Assert.Null(loser.Session);

            // Nothing revoked: no reuse detected and the winner's chain keeps working. (That the loser stores no replacement
            // needs an atomic SaveChanges: asserted on PostgreSQL, the in-memory provider is not transactional.)
            Assert.False(await context.DbAsync(db => db.SecurityAuditEvents.AnyAsync(e =>
                e.UserId == user.Id && e.EventType == SecurityAuditEventType.RefreshTokenReuseDetected)));
            Assert.False(await context.DbAsync(db => db.RefreshTokens.AnyAsync(t =>
                t.UserId == user.Id && t.RevocationReason != null && t.RevocationReason != RefreshTokenRevocationReason.Rotated)));
            var next = await context.RefreshSessionAsync(winner.Session!.RefreshToken);
            Assert.Equal(RefreshStatus.Succeeded, next.Status);

            // Existing audit classification, no new enum value: RefreshFailed / TokenRevoked.
            Assert.True(await context.DbAsync(db => db.SecurityAuditEvents.AnyAsync(e =>
                e.UserId == user.Id && e.EventType == SecurityAuditEventType.RefreshFailed
                && e.FailureReason == SecurityFailureReason.TokenRevoked)));
        }

        [Fact]
        public async Task TheNullableRefreshKeepsAnsweringALostRaceWithNull()
        {
            var barrier = new RefreshReadBarrier();
            await using var context = new SecurityTestContext(refreshReadBarrier: barrier);
            var user = await context.CreateUserAsync(SecurityRoles.Paramedic);
            var login = (await context.LoginAsync(user.Email, user.Password))!;

            barrier.Arm(2);
            var results = await Task.WhenAll(
                Task.Run(() => context.RefreshAsync(login.RefreshToken)),
                Task.Run(() => context.RefreshAsync(login.RefreshToken)));

            Assert.Equal(1, results.Count(r => r is not null));
            Assert.Equal(1, results.Count(r => r is null));
        }

        [Fact]
        public async Task ReplayingATokenAfterItsRotationCommittedIsStillReuseAndRevokesTheFamily()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);
            var login = (await context.LoginAsync(user.Email, user.Password))!;
            var t2 = (await context.RefreshSessionAsync(login.RefreshToken)).Session!;
            var t3 = (await context.RefreshSessionAsync(t2.RefreshToken)).Session!;

            var replay = await context.RefreshSessionAsync(login.RefreshToken);

            Assert.Equal(RefreshStatus.Failed, replay.Status);
            Assert.Null(replay.Session);
            Assert.True(await context.DbAsync(db => db.SecurityAuditEvents.AnyAsync(e =>
                e.UserId == user.Id && e.EventType == SecurityAuditEventType.RefreshTokenReuseDetected)));
            Assert.False(await context.DbAsync(db => db.RefreshTokens.AnyAsync(t => t.UserId == user.Id && t.RevokedAtUtc == null)));
            Assert.True(await context.DbAsync(db => db.RefreshTokens.AnyAsync(t =>
                t.UserId == user.Id && t.RevocationReason == RefreshTokenRevocationReason.ReuseDetected)));

            // The current descendant of the chain stops working too.
            Assert.Equal(RefreshStatus.Failed, (await context.RefreshSessionAsync(t3.RefreshToken)).Status);
            Assert.Equal(RefreshStatus.Failed, (await context.RefreshSessionAsync(t2.RefreshToken)).Status);
        }

        [Fact]
        public async Task ReplayRevokesOnlyThatFamily()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);
            var first = (await context.LoginAsync(user.Email, user.Password))!;
            var otherSession = (await context.LoginAsync(user.Email, user.Password))!;
            await context.RefreshSessionAsync(first.RefreshToken);

            Assert.Equal(RefreshStatus.Failed, (await context.RefreshSessionAsync(first.RefreshToken)).Status);

            Assert.Equal(RefreshStatus.Succeeded, (await context.RefreshSessionAsync(otherSession.RefreshToken)).Status);
        }

        [Fact]
        public async Task StoreTellsALostRotationApartFromAnyOtherRevocation()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);
            var rotatedLogin = (await context.LoginAsync(user.Email, user.Password))!;
            var loggedOutLogin = (await context.LoginAsync(user.Email, user.Password))!;
            await context.RefreshSessionAsync(rotatedLogin.RefreshToken);
            await context.AuthenticationAsync(auth => auth.LogoutAsync(loggedOutLogin.RefreshToken, ClientContext.Unknown, None));
            var tokens = await context.DbAsync(db => db.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync());
            var rotated = Assert.Single(tokens, t => t.RevocationReason == RefreshTokenRevocationReason.Rotated);
            var loggedOut = Assert.Single(tokens, t => t.RevocationReason == RefreshTokenRevocationReason.Logout);

            var afterRotation = await RotateAsync(context, rotated.Id, rotated.FamilyId, user.Id);
            var afterLogout = await RotateAsync(context, loggedOut.Id, loggedOut.FamilyId, user.Id);

            Assert.Equal(RefreshTokenRotationResult.LostConcurrencyRace, afterRotation);
            Assert.Equal(RefreshTokenRotationResult.Revoked, afterLogout);
            // Neither attempt stored a replacement.
            Assert.Equal(tokens.Count, await context.DbAsync(db => db.RefreshTokens.CountAsync(t => t.UserId == user.Id)));
        }

        private static Task<RefreshTokenRotationResult> RotateAsync(SecurityTestContext context, Guid tokenId, Guid familyId, Guid userId) =>
            context.RunAsync(sp => sp.GetRequiredService<IRefreshTokenStore>().RotateAsync(
                tokenId,
                new NewRefreshToken(userId, familyId, new string('a', 64), DateTimeOffset.UtcNow.AddDays(1), null),
                null,
                None));
    }
}
