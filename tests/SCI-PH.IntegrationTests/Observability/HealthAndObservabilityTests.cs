using System.Net;
using System.Net.Http.Json;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Infrastructure.Observability;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Web.BFF.Controllers;

namespace SCI_PH.IntegrationTests.Observability
{
    // Health probes and request observability (PR #23, integrated): /health and /health/live answer while the process runs,
    // /health/ready also needs PostgreSQL; probes are anonymous and never reveal internals. Every response carries an
    // X-Correlation-ID that is also in its request log line, and that line never contains a token, a password or a body.
    [Collection(PostgresCollection.Name)]
    public sealed class HealthAndObservabilityTests
    {
        private readonly PostgresFixture _postgres;

        public HealthAndObservabilityTests(PostgresFixture postgres) => _postgres = postgres;

        [SkippableFact]
        public async Task WithPostgresEveryProbeIsHealthy()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var client = host.Anonymous();

            foreach (var probe in new[] { "/health", "/health/live", "/health/ready" })
            {
                var response = await client.GetAsync(probe);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
            }
        }

        [Fact]
        public async Task WithoutPostgresOnlyReadinessFailsAndRevealsNothing()
        {
            var logs = new CapturingLoggerProvider();
            // A database nobody listens to: the process is alive, but it cannot serve traffic.
            await using var factory = new PostgresWebBffFactory("Host=127.0.0.1;Port=1;Database=sciph_unreachable;Username=nobody;Timeout=2", logs);
            var client = factory.CreateHttpsClient();

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
            var ready = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            var body = await ready.Content.ReadAsStringAsync();
            Assert.Equal("Unhealthy", body);
            Assert.DoesNotContain("sciph_unreachable", body);
            Assert.DoesNotContain("Exception", body);
        }

        [SkippableFact]
        public async Task EveryResponseCarriesACorrelationIdThatIsInItsLogLineWithoutSecrets()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinic = await host.CreateClinicAsync();
            var (account, _) = await host.CreateStaffAccountAsync(SecurityRoles.Doctor, [clinic]);

            var login = await host.Anonymous().PostAsJsonAsync("/api/web/auth/login", new LoginRequest(account.Email, account.Password));
            login.EnsureSuccessStatusCode();
            var session = await login.ReadAsAsync<WebLoginResponse>();
            var me = await host.Factory.CreateHttpsClient().WithBearer(session.Session!.AccessToken).GetAsync("/api/web/auth/me");
            var probe = await host.Anonymous().GetAsync("/health/ready");

            var correlation = Assert.Single(me.Headers.GetValues(RequestObservabilityMiddleware.CorrelationHeaderName));
            Assert.Matches("^[0-9a-f]{32}$", correlation);
            Assert.NotEqual(correlation, Assert.Single(login.Headers.GetValues(RequestObservabilityMiddleware.CorrelationHeaderName)));
            Assert.Single(probe.Headers.GetValues(RequestObservabilityMiddleware.CorrelationHeaderName));

            var logs = host.Logs.Text;
            Assert.Contains($"HTTP GET /api/web/auth/me responded 200", logs);
            Assert.Contains($"CorrelationId={correlation}", logs);
            // Probes are not logged; nothing secret ever is.
            Assert.DoesNotContain("HTTP GET /health", logs);
            Assert.DoesNotContain(session.Session.AccessToken, logs);
            Assert.DoesNotContain(account.Password, logs);
        }
    }
}
