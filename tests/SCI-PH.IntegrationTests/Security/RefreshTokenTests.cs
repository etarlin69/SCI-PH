using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Security
{
    public class RefreshTokenTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        [Fact]
        public async Task RefreshRotatesTheTokenAndLinksTheReplacement()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);
            var login = (await context.LoginAsync(user.Email, user.Password))!;

            var refreshed = await context.RefreshAsync(login.RefreshToken);

            Assert.NotNull(refreshed);
            Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
            var tokens = await context.DbAsync(db => db.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync());
            var old = Assert.Single(tokens, t => t.RevokedAtUtc != null);
            var current = Assert.Single(tokens, t => t.RevokedAtUtc == null);
            Assert.Equal(RefreshTokenRevocationReason.Rotated, old.RevocationReason);
            Assert.Equal(current.Id, old.ReplacedByTokenId);
            Assert.Equal(old.FamilyId, current.FamilyId);
        }

        [Fact]
        public async Task ReusingARotatedTokenFailsAndRevokesTheWholeSession()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Nurse);
            var login = (await context.LoginAsync(user.Email, user.Password))!;
            var refreshed = (await context.RefreshAsync(login.RefreshToken))!;

            Assert.Null(await context.RefreshAsync(login.RefreshToken));
            Assert.Null(await context.RefreshAsync(refreshed.RefreshToken));
            Assert.True(await context.DbAsync(db => db.SecurityAuditEvents.AnyAsync(e =>
                e.UserId == user.Id && e.EventType == SecurityAuditEventType.RefreshTokenReuseDetected)));
            Assert.False(await context.DbAsync(db => db.RefreshTokens.AnyAsync(t => t.UserId == user.Id && t.RevokedAtUtc == null)));
        }

        [Fact]
        public async Task ExpiredUnknownAndLoggedOutTokensAreRejected()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Paramedic);
            var expiring = (await context.LoginAsync(user.Email, user.Password))!;
            var loggedOut = (await context.LoginAsync(user.Email, user.Password))!;

            await context.AuthenticationAsync(auth => auth.LogoutAsync(loggedOut.RefreshToken, ClientContext.Unknown, None));
            context.Time.Advance(TimeSpan.FromDays(TestSettings.RefreshTokenLifetimeDays) + TimeSpan.FromMinutes(1));

            Assert.Null(await context.RefreshAsync(expiring.RefreshToken));
            Assert.Null(await context.RefreshAsync(loggedOut.RefreshToken));
            Assert.Null(await context.RefreshAsync("not-a-real-token"));
            var reasons = await context.DbAsync(db => db.SecurityAuditEvents
                .Where(e => e.EventType == SecurityAuditEventType.RefreshFailed).Select(e => e.FailureReason).ToListAsync());
            Assert.Contains(SecurityFailureReason.TokenExpired, reasons);
            Assert.Contains(SecurityFailureReason.TokenRevoked, reasons);
            Assert.Contains(SecurityFailureReason.TokenNotFound, reasons);
        }

        [Fact]
        public async Task LogoutIsIdempotent()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);
            var login = (await context.LoginAsync(user.Email, user.Password))!;

            await context.AuthenticationAsync(auth => auth.LogoutAsync(login.RefreshToken, ClientContext.Unknown, None));
            await context.AuthenticationAsync(auth => auth.LogoutAsync(login.RefreshToken, ClientContext.Unknown, None));
            await context.AuthenticationAsync(auth => auth.LogoutAsync("unknown", ClientContext.Unknown, None));

            Assert.Equal(1, await context.DbAsync(db => db.SecurityAuditEvents.CountAsync(e => e.EventType == SecurityAuditEventType.Logout)));
        }

        [Fact]
        public async Task RevokeAllSessionsEndsEverySessionOfTheUserOnly()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);
            var other = await context.CreateUserAsync(SecurityRoles.Doctor);
            var first = (await context.LoginAsync(user.Email, user.Password))!;
            var second = (await context.LoginAsync(user.Email, user.Password))!;
            var otherSession = (await context.LoginAsync(other.Email, other.Password))!;

            await context.AuthenticationAsync(auth => auth.RevokeAllSessionsAsync(user.Id, ClientContext.Unknown, None));

            Assert.Null(await context.RefreshAsync(first.RefreshToken));
            Assert.Null(await context.RefreshAsync(second.RefreshToken));
            Assert.NotNull(await context.RefreshAsync(otherSession.RefreshToken));
        }

        [Fact]
        public async Task DeactivatedAccountCannotRefresh()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Nurse);
            var login = (await context.LoginAsync(user.Email, user.Password))!;

            await context.AccountsAsync(accounts => accounts.DeactivateAsync(user.Id, Guid.NewGuid(), ClientContext.Unknown, None));

            Assert.Null(await context.RefreshAsync(login.RefreshToken));
            await context.AccountsAsync(accounts => accounts.ActivateAsync(user.Id, Guid.NewGuid(), ClientContext.Unknown, None));
            Assert.NotNull(await context.LoginAsync(user.Email, user.Password));
        }

        [Fact]
        public async Task OnlyTheHashIsPersisted()
        {
            await using var context = new SecurityTestContext();
            var user = await context.CreateUserAsync(SecurityRoles.Doctor);
            var login = (await context.LoginAsync(user.Email, user.Password))!;

            var stored = await context.DbAsync(db => db.RefreshTokens.SingleAsync(t => t.UserId == user.Id));

            Assert.NotEqual(login.RefreshToken, stored.TokenHash);
            Assert.Matches("^[0-9a-f]{64}$", stored.TokenHash);
            Assert.DoesNotContain(typeof(SCI_PH.Persistence.Identity.Models.RefreshToken).GetProperties(),
                property => property.Name is "Token" or "Value" or "RawToken");
        }
    }
}
