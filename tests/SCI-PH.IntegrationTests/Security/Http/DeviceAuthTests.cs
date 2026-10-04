using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Device.API.Controllers;
using SCI_PH.Infrastructure.Security;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.IntegrationTests.Security.Http
{
    public sealed class DeviceApiFactory : BffFactory<HeartbeatController>;

    // Real Device API host with its own DeviceApiKey scheme (no JWT, no human accounts).
    public sealed class DeviceAuthTests : IClassFixture<DeviceApiFactory>
    {
        private const string HeartbeatPath = "/api/device/heartbeat";
        private readonly DeviceApiFactory _factory;

        public DeviceAuthTests(DeviceApiFactory factory) => _factory = factory;

        [Fact]
        public async Task ValidDeviceKeyIsAcceptedAndRecordsTheHeartbeat()
        {
            var device = await ProvisionAsync(DeviceScopes.Heartbeat);

            var response = await Heartbeat(device.Code, device.Secret);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var stored = await DbAsync(db => db.DeviceCredentials.SingleAsync(d => d.Id == device.Id));
            Assert.Equal("1.2.3", stored.FirmwareVersion);
            Assert.NotNull(stored.LastSeenAtUtc);
            Assert.NotEqual(device.Secret, stored.SecretHash);
        }

        [Fact]
        public async Task MissingOrInvalidCredentialsAreRejected()
        {
            var device = await ProvisionAsync(DeviceScopes.Heartbeat);

            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateHttpsClient().PostAsJsonAsync(HeartbeatPath, Body)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Heartbeat(device.Code, "wrong-key")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Heartbeat("UNKNOWN-DEVICE", device.Secret)).StatusCode);
            Assert.True(await DbAsync(db => db.SecurityAuditEvents.AnyAsync(e =>
                e.EventType == SecurityAuditEventType.DeviceAuthenticationFailed && e.DeviceCredentialId == device.Id)));
        }

        [Fact]
        public async Task KeyInTheQueryStringOrOverHttpIsNotAccepted()
        {
            var device = await ProvisionAsync(DeviceScopes.Heartbeat);
            var http = _factory.CreateClient();
            http.DefaultRequestHeaders.Add(DeviceAuthenticationDefaults.DeviceIdHeader, device.Code);
            http.DefaultRequestHeaders.Add(DeviceAuthenticationDefaults.DeviceKeyHeader, device.Secret);

            var overHttp = await http.PostAsJsonAsync(HeartbeatPath, Body);
            var inQuery = await _factory.CreateHttpsClient().PostAsJsonAsync(
                $"{HeartbeatPath}?X-Device-Id={device.Code}&X-Device-Key={Uri.EscapeDataString(device.Secret)}", Body);

            Assert.Equal(HttpStatusCode.Unauthorized, overHttp.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, inQuery.StatusCode);
        }

        [Fact]
        public async Task RevokedDeviceIsRejected()
        {
            var device = await ProvisionAsync(DeviceScopes.Heartbeat);
            await WithStoreAsync(store => store.RevokeAsync(device.Id, CancellationToken.None));

            Assert.Equal(HttpStatusCode.Unauthorized, (await Heartbeat(device.Code, device.Secret)).StatusCode);
        }

        [Fact]
        public async Task RotationInvalidatesTheOldKeyImmediately()
        {
            var device = await ProvisionAsync(DeviceScopes.Heartbeat);
            var newSecret = await RotateAsync(device.Id);

            Assert.Equal(HttpStatusCode.Unauthorized, (await Heartbeat(device.Code, device.Secret)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Heartbeat(device.Code, newSecret)).StatusCode);
        }

        [Fact]
        public async Task ScopesAreEnforcedAndHumanTokensAreNotAccepted()
        {
            var device = await ProvisionAsync(DeviceScopes.FingerprintIdentify);
            var bearer = _factory.CreateHttpsClient().WithBearer("eyJhbGciOiJIUzI1NiJ9.e30.signature");

            Assert.Equal(HttpStatusCode.Forbidden, (await Heartbeat(device.Code, device.Secret)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.PostAsJsonAsync(HeartbeatPath, Body)).StatusCode);
        }

        [Fact]
        public void DeviceScopesNeverOverlapHumanOrClinicalPermissions()
        {
            Assert.Empty(DeviceScopes.All.Intersect(Permissions.All));
            Assert.All(DeviceScopes.All, scope => Assert.StartsWith("Device.", scope));
        }

        private static readonly DeviceHeartbeatRequest Body = new("1.2.3", 42);

        private Task<HttpResponseMessage> Heartbeat(string code, string key)
        {
            var client = _factory.CreateHttpsClient();
            client.DefaultRequestHeaders.Add(DeviceAuthenticationDefaults.DeviceIdHeader, code);
            client.DefaultRequestHeaders.Add(DeviceAuthenticationDefaults.DeviceKeyHeader, key);
            return client.PostAsJsonAsync(HeartbeatPath, Body);
        }

        private async Task<(Guid Id, string Code, string Secret)> ProvisionAsync(string scope)
        {
            await using var scopeProvider = _factory.Services.CreateAsyncScope();
            var secrets = scopeProvider.ServiceProvider.GetRequiredService<ISecureTokenGenerator>();
            var code = $"DEV-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
            var secret = secrets.Generate(32);
            var id = await scopeProvider.ServiceProvider.GetRequiredService<IDeviceCredentialStore>()
                .AddAsync(new NewDeviceCredential(code, "Test device", [scope], secrets.Hash(secret), null), CancellationToken.None);
            return (id!.Value, code, secret);
        }

        private async Task<string> RotateAsync(Guid id)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecureTokenGenerator>();
            var secret = secrets.Generate(32);
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IDeviceCredentialStore>().ReplaceSecretAsync(id, secrets.Hash(secret), CancellationToken.None));
            return secret;
        }

        private async Task WithStoreAsync(Func<IDeviceCredentialStore, Task> action)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<IDeviceCredentialStore>());
        }

        private async Task<T> DbAsync<T>(Func<IdentityDbContext, Task<T>> query)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            return await query(scope.ServiceProvider.GetRequiredService<IdentityDbContext>());
        }
    }
}
