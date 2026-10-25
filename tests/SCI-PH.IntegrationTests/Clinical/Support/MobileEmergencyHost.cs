using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Mobile.BFF.Controllers;

namespace SCI_PH.IntegrationTests.Clinical.Support
{
    // The real Mobile BFF over the same migrated PostgreSQL database as a ClinicalApiHost (the Web BFF): accounts,
    // clinics and staff are prepared through the web host, the paramedic signs in on the mobile one.
    internal sealed class PostgresMobileBffFactory(string connectionString) : BffFactory<AuthController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.UsePostgresStores(connectionString));
        }
    }

    internal sealed class MobileEmergencyHost : IAsyncDisposable
    {
        private readonly PostgresMobileBffFactory _mobile;

        private MobileEmergencyHost(ClinicalApiHost web)
        {
            Web = web;
            _mobile = new PostgresMobileBffFactory(web.ConnectionString);
        }

        public ClinicalApiHost Web { get; }

        public string ConnectionString => Web.ConnectionString;

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

        public async ValueTask DisposeAsync()
        {
            await _mobile.DisposeAsync();
            await Web.DisposeAsync();
        }
    }
}
