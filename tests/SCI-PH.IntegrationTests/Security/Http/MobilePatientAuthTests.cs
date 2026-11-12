using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Mobile.BFF.Controllers;
using SCI_PH.Persistence.Contexts;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.IntegrationTests.Security.Http
{
    // A Patient account uses the same Mobile BFF authentication as the staff (ADR-015): password, optional MFA, refresh
    // rotation, logout and session revocation. It gets the Patient role and no permission at all (in particular not
    // Emergencies.Operate), and its sessions end like any other.
    public sealed class MobilePatientAuthTests : IClassFixture<MobileBffFactory>
    {
        private const string Base = "/api/mobile/auth";
        private const string Me = "/api/mobile/patient/me";
        private readonly MobileBffFactory _factory;

        public MobilePatientAuthTests(MobileBffFactory factory) => _factory = factory;

        [Fact]
        public async Task APatientSignsInWithTheAccountAndHasNoPermission()
        {
            var patient = await CreateLinkedPatientAsync();

            var login = await Client().PostAsJsonAsync($"{Base}/login", new LoginRequest(patient.Email, patient.Password));
            var session = (await login.ReadAsAsync<MobileLoginResponse>()).Session!;
            var me = await Client().WithBearer(session.AccessToken).GetFromJsonAsync<UserAccount>($"{Base}/me");

            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.Equal([SecurityRoles.Patient], session.User.Roles);
            Assert.Empty(session.User.Permissions);
            Assert.DoesNotContain(Permissions.Emergencies.Operate, me!.Permissions);
            Assert.Equal(patient.Id, me.Id);
            Assert.Equal(HttpStatusCode.OK, (await Client().WithBearer(session.AccessToken).GetAsync(Me)).StatusCode);
        }

        [Fact]
        public async Task AWrongPasswordOrADeactivatedPatientAccountGetsTheSame401()
        {
            var patient = await CreateLinkedPatientAsync();
            var deactivated = await CreateLinkedPatientAsync();
            var session = await LoginAsync(deactivated);
            await using (var scope = _factory.Services.CreateAsyncScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = (await users.FindByIdAsync(deactivated.Id.ToString()))!;
                user.Deactivate(DateTimeOffset.UtcNow);
                await users.UpdateAsync(user);
            }

            var wrong = await Client().PostAsJsonAsync($"{Base}/login", new LoginRequest(patient.Email, TestSettings.NewPassword()));
            var inactive = await Client().PostAsJsonAsync($"{Base}/login", new LoginRequest(deactivated.Email, deactivated.Password));
            var refresh = await Client().PostAsJsonAsync($"{Base}/refresh", new RefreshTokenRequest(session.RefreshToken));

            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, inactive.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        }

        [Fact]
        public async Task RefreshRotatesAndLogoutAndSessionRevocationEndThePatientSession()
        {
            var patient = await CreateLinkedPatientAsync();
            var first = await LoginAsync(patient);
            var second = await LoginAsync(patient);

            var refreshed = await (await Client().PostAsJsonAsync($"{Base}/refresh", new RefreshTokenRequest(first.RefreshToken))).ReadAsAsync<AuthenticationResult>();
            var replay = await Client().PostAsJsonAsync($"{Base}/refresh", new RefreshTokenRequest(first.RefreshToken));
            Assert.NotEqual(first.RefreshToken, refreshed.RefreshToken);
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Client().WithBearer(refreshed.AccessToken).GetAsync(Me)).StatusCode);

            var logout = await Client().PostAsJsonAsync($"{Base}/logout", new RefreshTokenRequest(second.RefreshToken));
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client().PostAsJsonAsync($"{Base}/refresh", new RefreshTokenRequest(second.RefreshToken))).StatusCode);

            var third = await LoginAsync(patient);
            var revoke = await Client().WithBearer(third.AccessToken).PostAsync($"{Base}/revoke-sessions", null);
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client().PostAsJsonAsync($"{Base}/refresh", new RefreshTokenRequest(third.RefreshToken))).StatusCode);
        }

        [Fact]
        public async Task APatientWithMfaGetsTheSessionOnlyAfterTheSecondFactor()
        {
            var patient = await CreateLinkedPatientAsync();
            string key;
            await using (var scope = _factory.Services.CreateAsyncScope())
            {
                var authenticator = scope.ServiceProvider.GetRequiredService<IAuthenticatorService>();
                key = (await authenticator.BeginSetupAsync(patient.Id, CancellationToken.None))!.SharedKey;
                Assert.NotNull(await authenticator.EnableAsync(patient.Id, Totp.Code(key), CancellationToken.None));
            }

            var login = await (await Client().PostAsJsonAsync($"{Base}/login", new LoginRequest(patient.Email, patient.Password))).ReadAsAsync<MobileLoginResponse>();
            var challengeAsBearer = await Client().WithBearer(login.MfaChallenge!.Token).GetAsync(Me);
            var verified = await Client().PostAsJsonAsync($"{Base}/mfa/verify", new MfaVerifyRequest(login.MfaChallenge.Token, Totp.Code(key), null));
            var session = await verified.ReadAsAsync<AuthenticationResult>();

            Assert.True(login.MfaRequired);
            Assert.Null(login.Session);
            Assert.Equal(HttpStatusCode.Unauthorized, challengeAsBearer.StatusCode);
            Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Client().WithBearer(session.AccessToken).GetAsync(Me)).StatusCode);
        }

        private HttpClient Client() => _factory.CreateHttpsClient();

        private async Task<TestAccount> CreateLinkedPatientAsync()
        {
            var account = await _factory.CreateUserAsync(SecurityRoles.Patient);
            await using var scope = _factory.Services.CreateAsyncScope();
            await PatientRecordSeed.SeedAsync(scope.ServiceProvider.GetRequiredService<ClinicalDbContext>(), "Paciente", account.Id);
            return account;
        }

        private async Task<AuthenticationResult> LoginAsync(TestAccount account)
        {
            var response = await Client().PostAsJsonAsync($"{Base}/login", new LoginRequest(account.Email, account.Password));
            response.EnsureSuccessStatusCode();
            return (await response.ReadAsAsync<MobileLoginResponse>()).Session!;
        }
    }
}
