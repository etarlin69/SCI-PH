using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Security.Http
{
    public sealed class LowRateLimitWebFactory : BffFactory<SCI_PH.Web.BFF.Controllers.AuthController>
    {
        protected override IDictionary<string, string?> Settings =>
            TestSettings.Create(new Dictionary<string, string?> { ["RateLimiting:AuthenticationPermitLimit"] = "3" });
    }

    // OpenAPI is only mapped in Development.
    public sealed class DevelopmentWebFactory : BffFactory<SCI_PH.Web.BFF.Controllers.AuthController>
    {
        protected override string Environment => "Development";
    }

    public sealed class DevelopmentMobileFactory : BffFactory<SCI_PH.Mobile.BFF.Controllers.AuthController>
    {
        protected override string Environment => "Development";
    }

    public sealed class DevelopmentDeviceFactory : BffFactory<SCI_PH.Device.API.Controllers.HeartbeatController>
    {
        protected override string Environment => "Development";
    }

    public sealed class HostConfigurationTests
    {
        [Fact]
        public async Task LoginIsRateLimitedWithA429Problem()
        {
            await using var factory = new LowRateLimitWebFactory();
            var client = factory.CreateHttpsClient();
            var request = new LoginRequest("nobody@sci-ph.test", TestSettings.NewPassword());

            var statuses = new List<HttpStatusCode>();
            for (var attempt = 0; attempt < 4; attempt++)
            {
                statuses.Add((await client.PostAsJsonAsync("/api/web/auth/login", request)).StatusCode);
            }

            var limited = await client.PostAsJsonAsync("/api/web/auth/login", request);
            Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        }

        [Fact]
        public async Task WebOpenApiDocumentsBearerOnlyOnProtectedOperations()
        {
            await using var factory = new DevelopmentWebFactory();
            using var document = JsonDocument.Parse(await factory.CreateHttpsClient().GetStringAsync("/openapi/v1.json"));
            var root = document.RootElement;

            var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
            Assert.Equal("http", scheme.GetProperty("type").GetString());
            Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
            Assert.False(Operation(root, "/api/web/auth/login", "post").TryGetProperty("security", out _));
            Assert.False(Operation(root, "/api/web/auth/forgot-password", "post").TryGetProperty("security", out _));
            Assert.True(Operation(root, "/api/web/auth/me", "get").TryGetProperty("security", out _));
            var roles = Operation(root, "/api/web/admin/roles", "get");
            Assert.True(roles.GetProperty("responses").TryGetProperty("403", out _));
            Assert.DoesNotContain(TestSettings.SigningKey, root.GetRawText());
        }

        [Fact]
        public async Task MobileOpenApiDocumentsBearer()
        {
            await using var factory = new DevelopmentMobileFactory();
            using var document = JsonDocument.Parse(await factory.CreateHttpsClient().GetStringAsync("/openapi/v1.json"));

            Assert.True(document.RootElement.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));
            Assert.True(Operation(document.RootElement, "/api/mobile/auth/me", "get").TryGetProperty("security", out _));
            Assert.False(Operation(document.RootElement, "/api/mobile/auth/refresh", "post").TryGetProperty("security", out _));
        }

        [Fact]
        public async Task DeviceOpenApiDocumentsTheDeviceKeyScheme()
        {
            await using var factory = new DevelopmentDeviceFactory();
            using var document = JsonDocument.Parse(await factory.CreateHttpsClient().GetStringAsync("/openapi/v1.json"));

            var schemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");
            Assert.Equal("apiKey", schemes.GetProperty("X-Device-Key").GetProperty("type").GetString());
            Assert.False(schemes.TryGetProperty("Bearer", out _));
            Assert.True(Operation(document.RootElement, "/api/device/heartbeat", "post").TryGetProperty("security", out _));
        }

        [Fact]
        public async Task HealthChecksStayAnonymous()
        {
            await using var web = new DevelopmentWebFactory();
            await using var device = new DevelopmentDeviceFactory();

            Assert.Equal(HttpStatusCode.OK, (await web.CreateHttpsClient().GetAsync("/health")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await device.CreateHttpsClient().GetAsync("/health")).StatusCode);
        }

        private static JsonElement Operation(JsonElement root, string path, string method) =>
            root.GetProperty("paths").GetProperty(path).GetProperty(method);
    }
}
