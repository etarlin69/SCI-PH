using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Infrastructure.Web;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Web.BFF.Controllers;
using SCI_PH.Web.BFF.Security;

namespace SCI_PH.IntegrationTests.Security.Http
{
    public sealed class WebBffFactory : BffFactory<AuthController>;

    public sealed class WebAuthTests : IClassFixture<WebBffFactory>
    {
        private const string LoginPath = "/api/web/auth/login";
        private const string RefreshPath = "/api/web/auth/refresh";
        private readonly WebBffFactory _factory;

        public WebAuthTests(WebBffFactory factory) => _factory = factory;

        [Fact]
        public async Task LoginSetsAHardenedRefreshCookieAndKeepsTheTokenOutOfTheBody()
        {
            var user = await _factory.CreateUserAsync(SecurityRoles.Doctor);

            var response = await _factory.CreateHttpsClient().PostAsJsonAsync(LoginPath, new LoginRequest(user.Email, user.Password));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(RefreshTokenCookie.Name + "=", StringComparison.Ordinal));
            var attributes = cookie.ToLowerInvariant();
            Assert.Contains("httponly", attributes);
            Assert.Contains("secure", attributes);
            Assert.Contains("samesite=strict", attributes);
            Assert.Contains("path=/", attributes);
            Assert.DoesNotContain("domain=", attributes);
            Assert.Contains("expires=", attributes);

            var cookieValue = cookie.Split(';')[0][(RefreshTokenCookie.Name.Length + 1)..];
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("refreshToken", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(cookieValue, body);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }

        [Fact]
        public async Task RefreshReadsOnlyTheCookieAndRotatesIt()
        {
            var user = await _factory.CreateUserAsync(SecurityRoles.Nurse);
            var client = _factory.CreateHttpsClient(handleCookies: true);
            var login = await client.PostAsJsonAsync(LoginPath, new LoginRequest(user.Email, user.Password));
            var firstCookie = RefreshCookieValue(login);

            var refreshed = await client.PostAsync(RefreshPath, null);
            var secondCookie = RefreshCookieValue(refreshed);

            Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
            Assert.NotEqual(firstCookie, secondCookie);
            Assert.False(string.IsNullOrEmpty((await refreshed.ReadAsAsync<WebSession>()).AccessToken));

            // Replaying the rotated cookie is rejected and ends the whole session.
            var replay = _factory.CreateHttpsClient();
            replay.DefaultRequestHeaders.Add("Cookie", $"{RefreshTokenCookie.Name}={firstCookie}");
            Assert.Equal(HttpStatusCode.Unauthorized, (await replay.PostAsync(RefreshPath, null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(RefreshPath, null)).StatusCode);
        }

        [Fact]
        public async Task RefreshIgnoresATokenSentInTheBody()
        {
            var user = await _factory.CreateUserAsync(SecurityRoles.Nurse);
            var login = await _factory.CreateHttpsClient().PostAsJsonAsync(LoginPath, new LoginRequest(user.Email, user.Password));

            var response = await _factory.CreateHttpsClient().PostAsJsonAsync(RefreshPath, new RefreshTokenRequest(RefreshCookieValue(login)));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task LogoutRevokesTheSessionAndDeletesTheCookie()
        {
            var user = await _factory.CreateUserAsync(SecurityRoles.Doctor);
            var client = _factory.CreateHttpsClient(handleCookies: true);
            await client.PostAsJsonAsync(LoginPath, new LoginRequest(user.Email, user.Password));

            var logout = await client.PostAsync("/api/web/auth/logout", null);
            var again = await client.PostAsync("/api/web/auth/logout", null);

            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
            var deletion = Assert.Single(logout.Headers.GetValues("Set-Cookie"), c => c.StartsWith(RefreshTokenCookie.Name + "=;", StringComparison.Ordinal));
            Assert.Contains("expires=Thu, 01 Jan 1970", deletion, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(RefreshPath, null)).StatusCode);
        }

        [Fact]
        public async Task CookieEndpointsRequireTheCsrfHeaderAndAnAllowedOrigin()
        {
            var user = await _factory.CreateUserAsync(SecurityRoles.Doctor);
            var withoutHeader = _factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
            var foreignOrigin = _factory.CreateHttpsClient();
            foreignOrigin.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
            var allowedOrigin = _factory.CreateHttpsClient();
            allowedOrigin.DefaultRequestHeaders.Add("Origin", TestSettings.FrontendOrigin);
            var request = new LoginRequest(user.Email, user.Password);

            Assert.Equal(HttpStatusCode.Forbidden, (await withoutHeader.PostAsJsonAsync(LoginPath, request)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await foreignOrigin.PostAsJsonAsync(LoginPath, request)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await withoutHeader.PostAsync(RefreshPath, null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await allowedOrigin.PostAsJsonAsync(LoginPath, request)).StatusCode);
        }

        [Fact]
        public async Task CorsAllowsCredentialsOnlyForConfiguredOrigins()
        {
            var client = _factory.CreateHttpsClient();

            var allowed = await client.SendAsync(Preflight(TestSettings.FrontendOrigin));
            var foreign = await client.SendAsync(Preflight("https://attacker.example"));

            Assert.Equal(TestSettings.FrontendOrigin, Assert.Single(allowed.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.Equal("true", Assert.Single(allowed.Headers.GetValues("Access-Control-Allow-Credentials")));
            Assert.False(foreign.Headers.Contains("Access-Control-Allow-Origin"));
        }

        [Fact]
        public async Task ProtectedEndpointsReturn401And403()
        {
            var nurse = _factory.CreateHttpsClient().WithBearer(await AccessTokenAsync(SecurityRoles.Nurse));
            var admin = _factory.CreateHttpsClient().WithBearer(await AccessTokenAsync(SecurityRoles.Administrator));

            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateHttpsClient().GetAsync("/api/web/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nurse.GetAsync("/api/web/admin/roles")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nurse.GetAsync("/api/web/admin/users")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/web/admin/roles")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/web/admin/users")).StatusCode);
        }

        [Fact]
        public async Task InvalidLoginIsAGeneric401ProblemWithSecurityHeaders()
        {
            var response = await _factory.CreateHttpsClient().PostAsJsonAsync(LoginPath, new LoginRequest("nobody@sci-ph.test", TestSettings.NewPassword()));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("Authentication failed.", await response.Content.ReadAsStringAsync());
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        }

        [Fact]
        public async Task MeReturnsTheAccountWithoutSensitiveFields()
        {
            var client = _factory.CreateHttpsClient().WithBearer(await AccessTokenAsync(SecurityRoles.Patient));

            var body = await client.GetStringAsync("/api/web/auth/me");

            using var document = JsonDocument.Parse(body);
            Assert.Equal([SecurityRoles.Patient], document.RootElement.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
            Assert.Empty(document.RootElement.GetProperty("permissions").EnumerateArray());
            Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("securityStamp", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task MfaChallengeIsNotAnAccessToken()
        {
            var user = await _factory.CreateUserAsync(SecurityRoles.Doctor);
            await EnableMfaAsync(user.Id);
            var client = _factory.CreateHttpsClient(handleCookies: true);

            var login = await (await client.PostAsJsonAsync(LoginPath, new LoginRequest(user.Email, user.Password))).ReadAsAsync<WebLoginResponse>();
            var challengeCaller = _factory.CreateHttpsClient().WithBearer(login.MfaChallenge!.Token);
            var verify = await client.PostAsJsonAsync("/api/web/auth/mfa/verify",
                new MfaVerifyRequest(login.MfaChallenge.Token, await CurrentCodeAsync(user.Id), null));

            Assert.True(login.MfaRequired);
            Assert.Null(login.Session);
            Assert.Equal(HttpStatusCode.Unauthorized, (await challengeCaller.GetAsync("/api/web/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
            var session = await verify.ReadAsAsync<WebSession>();
            Assert.Equal(HttpStatusCode.OK, (await _factory.CreateHttpsClient().WithBearer(session.AccessToken).GetAsync("/api/web/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(RefreshPath, null)).StatusCode);
        }

        [Fact]
        public async Task MfaChallengeReplayGets401AndIssuesNoSecondCookie()
        {
            var user = await _factory.CreateUserAsync(SecurityRoles.Doctor);
            await EnableMfaAsync(user.Id);
            var client = _factory.CreateHttpsClient();
            var login = await (await client.PostAsJsonAsync(LoginPath, new LoginRequest(user.Email, user.Password))).ReadAsAsync<WebLoginResponse>();
            var code = await CurrentCodeAsync(user.Id);

            var first = await client.PostAsJsonAsync("/api/web/auth/mfa/verify", new MfaVerifyRequest(login.MfaChallenge!.Token, code, null));
            var replay = await client.PostAsJsonAsync("/api/web/auth/mfa/verify", new MfaVerifyRequest(login.MfaChallenge.Token, code, null));

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
            Assert.False(replay.Headers.Contains("Set-Cookie"));
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateHttpsClient().WithBearer(login.MfaChallenge.Token).GetAsync("/api/web/auth/me")).StatusCode);
        }

        [Fact]
        public async Task PublicRecoveryEndpointsAnswerTheSameForKnownAndUnknownEmails()
        {
            var user = await _factory.CreateUserAsync(SecurityRoles.Doctor);
            var client = _factory.CreateHttpsClient();

            var known = await client.PostAsJsonAsync("/api/web/auth/forgot-password", new EmailRequest(user.Email));
            var unknown = await client.PostAsJsonAsync("/api/web/auth/forgot-password", new EmailRequest("nobody@sci-ph.test"));
            var resend = await client.PostAsJsonAsync("/api/web/auth/resend-confirmation", new EmailRequest("nobody@sci-ph.test"));

            Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
            Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task DeviceProvisioningReturnsTheKeyOnceAndRequiresDevicesManage()
        {
            var admin = _factory.CreateHttpsClient().WithBearer(await AccessTokenAsync(SecurityRoles.Administrator));
            var nurse = _factory.CreateHttpsClient().WithBearer(await AccessTokenAsync(SecurityRoles.Nurse));
            var code = $"ESP32-{Guid.NewGuid():N}"[..20];
            var request = new ProvisionDeviceRequest(code, "Ambulancia 7", [DeviceScopes.Heartbeat]);

            var created = await admin.PostAsJsonAsync("/api/web/admin/devices", request);
            var secret = await created.ReadAsAsync<DeviceSecret>();
            var list = await admin.GetStringAsync("/api/web/admin/devices");
            var rotated = await (await admin.PostAsync($"/api/web/admin/devices/{secret.Id}/rotate-secret", null)).ReadAsAsync<DeviceSecret>();

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal(code.ToUpperInvariant(), secret.DeviceCode);
            Assert.DoesNotContain(secret.Secret, list);
            Assert.DoesNotContain("secret", list, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(secret.Secret, rotated.Secret);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/web/admin/devices", request)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await nurse.PostAsJsonAsync("/api/web/admin/devices", request)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/web/admin/devices/{secret.Id}/revoke", null)).StatusCode);
        }

        private static HttpRequestMessage Preflight(string origin)
        {
            var request = new HttpRequestMessage(HttpMethod.Options, LoginPath);
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            request.Headers.Add("Access-Control-Request-Headers", $"content-type,{FrontendCors.CsrfHeaderName}");
            return request;
        }

        private static string RefreshCookieValue(HttpResponseMessage response) =>
            response.Headers.GetValues("Set-Cookie")
                .Single(c => c.StartsWith(RefreshTokenCookie.Name + "=", StringComparison.Ordinal))
                .Split(';')[0][(RefreshTokenCookie.Name.Length + 1)..];

        private async Task<string> AccessTokenAsync(string role)
        {
            var user = await _factory.CreateUserAsync(role);
            var response = await _factory.CreateHttpsClient().PostAsJsonAsync(LoginPath, new LoginRequest(user.Email, user.Password));
            return (await response.ReadAsAsync<WebLoginResponse>()).Session!.AccessToken;
        }

        private async Task EnableMfaAsync(Guid userId)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var authenticator = scope.ServiceProvider.GetRequiredService<IAuthenticatorService>();
            var setup = (await authenticator.BeginSetupAsync(userId, CancellationToken.None))!;
            Assert.NotNull(await authenticator.EnableAsync(userId, Totp.Code(setup.SharedKey), CancellationToken.None));
            _keys[userId] = setup.SharedKey;
        }

        private readonly Dictionary<Guid, string> _keys = [];

        private Task<string> CurrentCodeAsync(Guid userId) => Task.FromResult(Totp.Code(_keys[userId]));
    }
}
