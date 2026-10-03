using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Mobile.BFF.Controllers;
using SCI_PH.Persistence.Identity;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.IntegrationTests.Security.Http
{
    public sealed class MobileBffFactory : BffFactory<AuthController>;

    // Real Mobile BFF host: the refresh token travels in the body (for MAUI SecureStorage), never in a cookie.
    public sealed class MobileAuthTests : IClassFixture<MobileBffFactory>
    {
        private const string Base = "/api/mobile/auth";
        private readonly MobileBffFactory _factory;

        public MobileAuthTests(MobileBffFactory factory) => _factory = factory;

        [Fact]
        public async Task LoginReturnsBothTokensInTheBodyAndSetsNoCookie()
        {
            var user = await _factory.CreateUserAsync(SecurityRoles.Paramedic);

            var response = await Client().PostAsJsonAsync($"{Base}/login", new LoginRequest(user.Email, user.Password));
            var login = await response.ReadAsAsync<MobileLoginResponse>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));
            Assert.False(login.MfaRequired);
            Assert.False(string.IsNullOrEmpty(login.Session!.AccessToken));
            Assert.False(string.IsNullOrEmpty(login.Session.RefreshToken));
            Assert.Contains(Permissions.Patients.Identify, login.Session.User.Permissions);
        }

        [Fact]
        public async Task MfaChallengeIsSingleUseAndNeverAnAccessToken()
        {
            var user = await _factory.CreateUserAsync(SecurityRoles.Paramedic);
            string key;
            await using (var scope = _factory.Services.CreateAsyncScope())
            {
                var authenticator = scope.ServiceProvider.GetRequiredService<IAuthenticatorService>();
                key = (await authenticator.BeginSetupAsync(user.Id, CancellationToken.None))!.SharedKey;
                Assert.NotNull(await authenticator.EnableAsync(user.Id, Totp.Code(key), CancellationToken.None));
            }

            var login = await (await Client().PostAsJsonAsync($"{Base}/login", new LoginRequest(user.Email, user.Password))).ReadAsAsync<MobileLoginResponse>();
            var challenge = login.MfaChallenge!.Token;
            var asBearer = await Client().WithBearer(challenge).GetAsync($"{Base}/me");
            var first = await Client().PostAsJsonAsync($"{Base}/mfa/verify", new MfaVerifyRequest(challenge, Totp.Code(key), null));
            var sameCode = await Client().PostAsJsonAsync($"{Base}/mfa/verify", new MfaVerifyRequest(challenge, Totp.Code(key), null));
            var otherCode = await Client().PostAsJsonAsync($"{Base}/mfa/verify", new MfaVerifyRequest(challenge, Totp.Code(key, stepOffset: -1), null));

            Assert.True(login.MfaRequired);
            Assert.Equal(HttpStatusCode.Unauthorized, asBearer.StatusCode);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, sameCode.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, otherCode.StatusCode);
            var session = await first.ReadAsAsync<AuthenticationResult>();
            Assert.Equal(HttpStatusCode.OK, (await Client().WithBearer(session.AccessToken).GetAsync($"{Base}/me")).StatusCode);
        }

        [Fact]
        public async Task WrongPasswordUnknownInactiveAndLockedAccountsGetTheSame401()
        {
            var user = await _factory.CreateUserAsync(SecurityRoles.Nurse);
            var inactive = await _factory.CreateUserAsync(SecurityRoles.Nurse);
            var locked = await _factory.CreateUserAsync(SecurityRoles.Nurse);
            await using (var scope = _factory.Services.CreateAsyncScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var inactiveUser = (await users.FindByIdAsync(inactive.Id.ToString()))!;
                inactiveUser.Deactivate(DateTimeOffset.UtcNow);
                await users.UpdateAsync(inactiveUser);
            }

            for (var attempt = 0; attempt < IdentityPolicy.MaxFailedAccessAttempts; attempt++)
            {
                await Client().PostAsJsonAsync($"{Base}/login", new LoginRequest(locked.Email, TestSettings.NewPassword()));
            }

            var responses = new[]
            {
                await Client().PostAsJsonAsync($"{Base}/login", new LoginRequest(user.Email, TestSettings.NewPassword())),
                await Client().PostAsJsonAsync($"{Base}/login", new LoginRequest("nobody@sci-ph.test", TestSettings.NewPassword())),
                await Client().PostAsJsonAsync($"{Base}/login", new LoginRequest(inactive.Email, inactive.Password)),
                await Client().PostAsJsonAsync($"{Base}/login", new LoginRequest(locked.Email, locked.Password))
            };

            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
            var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
            Assert.Single(bodies.Select(b => b.Split("\"traceId\"")[0]).Distinct());
        }

        [Fact]
        public async Task ProtectedEndpointsRequireAToken()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync($"{Base}/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client().PostAsync($"{Base}/revoke-sessions", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client().PostAsJsonAsync($"{Base}/change-password",
                new ChangePasswordRequest(TestSettings.NewPassword(), TestSettings.NewPassword()))).StatusCode);
        }

        [Fact]
        public async Task MeRefreshRotationAndLogoutWork()
        {
            var (user, session) = await LoginAsync(SecurityRoles.Doctor);

            var me = await Client().WithBearer(session.AccessToken).GetFromJsonAsync<UserAccount>($"{Base}/me");
            var refreshed = await (await Client().PostAsJsonAsync($"{Base}/refresh", new RefreshTokenRequest(session.RefreshToken))).ReadAsAsync<AuthenticationResult>();
            var replay = await Client().PostAsJsonAsync($"{Base}/refresh", new RefreshTokenRequest(session.RefreshToken));
            var logoutAfterReplay = await Client().PostAsJsonAsync($"{Base}/logout", new RefreshTokenRequest(refreshed.RefreshToken));
            var afterLogout = await Client().PostAsJsonAsync($"{Base}/refresh", new RefreshTokenRequest(refreshed.RefreshToken));

            Assert.Equal(user.Id, me!.Id);
            Assert.NotEqual(session.RefreshToken, refreshed.RefreshToken);
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, logoutAfterReplay.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
        }

        [Fact]
        public async Task ChangePasswordEndsSessions()
        {
            var (user, session) = await LoginAsync(SecurityRoles.Nurse);
            var newPassword = TestSettings.NewPassword();
            var client = Client().WithBearer(session.AccessToken);

            var wrong = await client.PostAsJsonAsync($"{Base}/change-password", new ChangePasswordRequest(TestSettings.NewPassword(), newPassword));
            var weak = await client.PostAsJsonAsync($"{Base}/change-password", new ChangePasswordRequest(user.Password, "weak"));
            var changed = await client.PostAsJsonAsync($"{Base}/change-password", new ChangePasswordRequest(user.Password, newPassword));

            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client().PostAsJsonAsync($"{Base}/refresh", new RefreshTokenRequest(session.RefreshToken))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Client().PostAsJsonAsync($"{Base}/login", new LoginRequest(user.Email, newPassword))).StatusCode);
        }

        [Fact]
        public async Task RevokeSessionsEndsEveryRefreshToken()
        {
            var (user, first) = await LoginAsync(SecurityRoles.Paramedic);
            var second = (await (await Client().PostAsJsonAsync($"{Base}/login", new LoginRequest(user.Email, user.Password))).ReadAsAsync<MobileLoginResponse>()).Session!;

            var revoke = await Client().WithBearer(first.AccessToken).PostAsync($"{Base}/revoke-sessions", null);

            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client().PostAsJsonAsync($"{Base}/refresh", new RefreshTokenRequest(first.RefreshToken))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client().PostAsJsonAsync($"{Base}/refresh", new RefreshTokenRequest(second.RefreshToken))).StatusCode);
        }

        private HttpClient Client() => _factory.CreateHttpsClient();

        private async Task<(TestAccount User, AuthenticationResult Session)> LoginAsync(string role)
        {
            var user = await _factory.CreateUserAsync(role);
            var response = await Client().PostAsJsonAsync($"{Base}/login", new LoginRequest(user.Email, user.Password));
            return (user, (await response.ReadAsAsync<MobileLoginResponse>()).Session!);
        }
    }
}
