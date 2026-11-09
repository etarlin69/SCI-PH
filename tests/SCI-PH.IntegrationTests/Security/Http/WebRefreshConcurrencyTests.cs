using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Persistence.Contexts;
using SCI_PH.Web.BFF.Controllers;
using SCI_PH.Web.BFF.Security;

namespace SCI_PH.IntegrationTests.Security.Http
{
    // Web BFF host whose refresh-token reads can be lined up (RefreshReadBarrier) to reproduce two tabs refreshing the
    // same cookie at the same time.
    public sealed class WebBffRaceFactory : BffFactory<AuthController>
    {
        internal RefreshReadBarrier Barrier { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddRefreshReadBarrier(Barrier));
        }

        internal async Task<T> DbAsync<T>(Func<IdentityDbContext, Task<T>> query)
        {
            await using var scope = Services.CreateAsyncScope();
            return await query(scope.ServiceProvider.GetRequiredService<IdentityDbContext>());
        }
    }

    public sealed class WebRefreshConcurrencyTests : IClassFixture<WebBffRaceFactory>
    {
        private const string LoginPath = "/api/web/auth/login";
        private const string RefreshPath = "/api/web/auth/refresh";
        private readonly WebBffRaceFactory _factory;

        public WebRefreshConcurrencyTests(WebBffRaceFactory factory) => _factory = factory;

        [Fact]
        public async Task TwoTabsRefreshingTheSameCookieGetOne200AndOneHarmless409()
        {
            var (user, original) = await LoginAsync(SecurityRoles.Nurse);
            var tabA = ClientWithCookie(original);
            var tabB = ClientWithCookie(original);

            _factory.Barrier.Arm(2);
            var responses = await Task.WhenAll(
                Task.Run(() => tabA.PostAsync(RefreshPath, null)),
                Task.Run(() => tabB.PostAsync(RefreshPath, null)));

            var ok = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            var conflict = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);

            // 200: a new refresh cookie (not a deletion) and an access token.
            var issued = RefreshCookieValue(ok);
            Assert.False(string.IsNullOrEmpty(issued));
            Assert.NotEqual(original, issued);
            Assert.False(string.IsNullOrEmpty((await ok.ReadAsAsync<WebSession>()).AccessToken));

            // 409: no Set-Cookie at all (neither a deletion nor a new token), no token in the body, a safe retryable code.
            Assert.False(conflict.Headers.Contains("Set-Cookie"));
            Assert.Equal("application/problem+json", conflict.Content.Headers.ContentType?.MediaType);
            var body = await conflict.Content.ReadAsStringAsync();
            using (var problem = JsonDocument.Parse(body))
            {
                Assert.Equal(409, problem.RootElement.GetProperty("status").GetInt32());
                Assert.Equal(AuthController.RefreshSupersededCode, problem.RootElement.GetProperty("code").GetString());
                Assert.False(problem.RootElement.TryGetProperty("accessToken", out _));
            }

            Assert.DoesNotContain(original, body);
            Assert.DoesNotContain(issued, body);

            // Nothing was revoked: no reuse detection and the new chain keeps working.
            Assert.False(await _factory.DbAsync(db => db.SecurityAuditEvents.AnyAsync(e =>
                e.UserId == user.Id && e.EventType == SecurityAuditEventType.RefreshTokenReuseDetected)));
            var next = await ClientWithCookie(issued).PostAsync(RefreshPath, null);
            Assert.Equal(HttpStatusCode.OK, next.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await ClientWithCookie(RefreshCookieValue(next)).PostAsync(RefreshPath, null)).StatusCode);
        }

        [Fact]
        public async Task ReplayingTheOriginalCookieAfterTheRotationCommittedIs401ReuseAndRevokesTheFamily()
        {
            var (user, t1) = await LoginAsync(SecurityRoles.Doctor);
            var rotated = await ClientWithCookie(t1).PostAsync(RefreshPath, null);
            Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
            var t2 = RefreshCookieValue(rotated);

            var replay = await ClientWithCookie(t1).PostAsync(RefreshPath, null);

            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
            var deletion = Assert.Single(replay.Headers.GetValues("Set-Cookie"), c => c.StartsWith(RefreshTokenCookie.Name + "=;", StringComparison.Ordinal));
            Assert.Contains("expires=Thu, 01 Jan 1970", deletion, StringComparison.OrdinalIgnoreCase);
            Assert.True(await _factory.DbAsync(db => db.SecurityAuditEvents.AnyAsync(e =>
                e.UserId == user.Id && e.EventType == SecurityAuditEventType.RefreshTokenReuseDetected)));
            Assert.False(await _factory.DbAsync(db => db.RefreshTokens.AnyAsync(t => t.UserId == user.Id && t.RevokedAtUtc == null)));

            // The legitimate descendant dies with its family.
            Assert.Equal(HttpStatusCode.Unauthorized, (await ClientWithCookie(t2).PostAsync(RefreshPath, null)).StatusCode);
        }

        [Fact]
        public async Task AnInvalidCookieStillGets401AndTheCookieIsDeleted()
        {
            var response = await ClientWithCookie("not-a-real-token").PostAsync(RefreshPath, null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(RefreshTokenCookie.Name + "=;", StringComparison.Ordinal));
        }

        private async Task<(TestAccount User, string Cookie)> LoginAsync(string role)
        {
            var user = await _factory.CreateUserAsync(role);
            var login = await _factory.CreateHttpsClient().PostAsJsonAsync(LoginPath, new LoginRequest(user.Email, user.Password));
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            return (user, RefreshCookieValue(login));
        }

        private HttpClient ClientWithCookie(string cookie)
        {
            var client = _factory.CreateHttpsClient();
            client.DefaultRequestHeaders.Add("Cookie", $"{RefreshTokenCookie.Name}={cookie}");
            return client;
        }

        private static string RefreshCookieValue(HttpResponseMessage response)
        {
            var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(RefreshTokenCookie.Name + "=", StringComparison.Ordinal));
            return cookie.Split(';')[0][(RefreshTokenCookie.Name.Length + 1)..];
        }
    }
}
