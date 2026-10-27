using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Device.API.Controllers;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Infrastructure.Security;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Mobile.BFF.Controllers;

namespace SCI_PH.IntegrationTests.Clinical.Support
{
    // The real Mobile BFF over the same migrated PostgreSQL database as a ClinicalApiHost (the Web BFF): accounts,
    // clinics and staff are prepared through the web host, the paramedic signs in on the mobile one. Every log line is
    // captured (Trace), so a test can check that nothing sensitive was written.
    internal sealed class PostgresMobileBffFactory(string connectionString, CapturingLoggerProvider logs) : BffFactory<AuthController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
            builder.ConfigureTestServices(services => services.UsePostgresStores(connectionString));
        }
    }

    // The real Device API (device key authentication, no JWT) over the same database: the fingerprint readers.
    internal sealed class PostgresDeviceApiFactory(string connectionString, CapturingLoggerProvider logs) : BffFactory<HeartbeatController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
            builder.ConfigureTestServices(services => services.UsePostgresStores(connectionString));
        }
    }

    // A provisioned device and its key (generated per test, never stored).
    internal sealed record TestReader(Guid Id, string Code, string Key);

    internal sealed class MobileEmergencyHost : IAsyncDisposable
    {
        private readonly PostgresMobileBffFactory _mobile;
        private readonly PostgresDeviceApiFactory _device;

        private MobileEmergencyHost(ClinicalApiHost web)
        {
            Web = web;
            _mobile = new PostgresMobileBffFactory(web.ConnectionString, Logs);
            _device = new PostgresDeviceApiFactory(web.ConnectionString, Logs);
        }

        public ClinicalApiHost Web { get; }

        public string ConnectionString => Web.ConnectionString;

        // Log lines of the Mobile BFF and the Device API.
        public CapturingLoggerProvider Logs { get; } = new();

        public static async Task<MobileEmergencyHost> StartAsync(PostgreSql.PostgresFixture postgres) =>
            new(await ClinicalApiHost.StartAsync(postgres));

        public HttpClient Anonymous() => _mobile.CreateHttpsClient();

        // Mobile login (no MFA configured on test accounts) of a new account with the role, optionally as clinical staff.
        public async Task<StaffSession> SignInAsync(
            string role,
            Clinic[] clinics,
            bool asClinicalStaff = true,
            ClinicalStaffType staffType = ClinicalStaffType.Paramedic)
        {
            var (account, staffId) = await Web.CreateStaffAccountAsync(role, clinics, asClinicalStaff, staffType: staffType);
            var login = await _mobile.CreateHttpsClient().PostAsJsonAsync("/api/mobile/auth/login", new LoginRequest(account.Email, account.Password));
            login.EnsureSuccessStatusCode();
            var session = await login.ReadAsAsync<MobileLoginResponse>();
            return new StaffSession(_mobile.CreateHttpsClient().WithBearer(session.Session!.AccessToken), account.Id, staffId);
        }

        // A device credential as the administration would provision it (key returned once, only its hash stored).
        public async Task<TestReader> ProvisionReaderAsync(params string[] scopes)
        {
            await using var scope = _device.Services.CreateAsyncScope();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecureTokenGenerator>();
            var code = $"ESP32-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
            var key = secrets.Generate(32);
            var id = await scope.ServiceProvider.GetRequiredService<IDeviceCredentialStore>().AddAsync(
                new NewDeviceCredential(code, $"Lector {code}", scopes.Length == 0 ? [DeviceScopes.FingerprintIdentify] : scopes, secrets.Hash(key), null),
                CancellationToken.None);
            return new TestReader(id!.Value, code, key);
        }

        public async Task RevokeReaderAsync(Guid id)
        {
            await using var scope = _device.Services.CreateAsyncScope();
            Assert.True(await scope.ServiceProvider.GetRequiredService<IDeviceCredentialStore>().RevokeAsync(id, CancellationToken.None));
        }

        // The Device API as that reader (X-Device-Id + X-Device-Key over HTTPS).
        public HttpClient Reader(TestReader reader) => Reader(reader.Code, reader.Key);

        public HttpClient Reader(string code, string key)
        {
            var client = _device.CreateHttpsClient();
            client.DefaultRequestHeaders.Add(DeviceAuthenticationDefaults.DeviceIdHeader, code);
            client.DefaultRequestHeaders.Add(DeviceAuthenticationDefaults.DeviceKeyHeader, key);
            return client;
        }

        public HttpClient DeviceAnonymous() => _device.CreateHttpsClient();

        public async ValueTask DisposeAsync()
        {
            await _device.DisposeAsync();
            await _mobile.DisposeAsync();
            await Web.DisposeAsync();
        }
    }
}
