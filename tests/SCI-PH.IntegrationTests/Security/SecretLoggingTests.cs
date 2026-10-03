using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Accounts;
using SCI_PH.Application.Features.Authentication;
using SCI_PH.Application.Features.Devices;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Security
{
    // Guard: runs every secret-handling flow with Trace logging and checks no secret value reaches the logs.
    public class SecretLoggingTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        [Fact]
        public async Task NoSecretIsEverLogged()
        {
            await using var context = new SecurityTestContext();
            var admin = await context.CreateUserAsync(SecurityRoles.Administrator);
            var secrets = new List<string> { admin.Password };

            var login = (await context.LoginAsync(admin.Email, admin.Password))!;
            var refreshed = (await context.RefreshAsync(login.RefreshToken))!;
            await context.LoginAsync(admin.Email, TestSettings.NewPassword());
            secrets.AddRange([login.AccessToken, login.RefreshToken, refreshed.RefreshToken]);

            var patientEmail = $"patient-{Guid.NewGuid():N}@sci-ph.test";
            var patientId = await context.CreatePatientAsync(patientEmail);
            await context.With<AccountInvitationService, InvitationResult>(s => s.InvitePatientAsync(patientId, admin.Id, ClientContext.Unknown, None));
            var invitationToken = FakeEmailSender.LinkParameter(context.Email.LastTo(patientEmail), "token");
            var patientPassword = TestSettings.NewPassword();
            await context.With<AccountInvitationService, AccountActivationResult>(s =>
                s.ActivateAsync(new ActivateAccountRequest(invitationToken, patientPassword), ClientContext.Unknown, None));
            secrets.AddRange([invitationToken, patientPassword]);

            await context.With<AccountRecoveryService>(s => s.RequestPasswordResetAsync(admin.Email, ClientContext.Unknown, None));
            var resetToken = FakeEmailSender.LinkParameter(context.Email.LastTo(admin.Email), "token");
            var newPassword = TestSettings.NewPassword();
            await context.With<AccountRecoveryService, PasswordChangeResult>(s =>
                s.ResetPasswordAsync(new ResetPasswordRequest(admin.Id, resetToken, newPassword), ClientContext.Unknown, None));
            secrets.AddRange([resetToken, newPassword]);

            var setup = (await context.With<MfaManagementService, MfaSetup?>(s => s.BeginSetupAsync(admin.Id, ClientContext.Unknown, None)))!;
            var codes = (await context.With<MfaManagementService, RecoveryCodes?>(s => s.EnableAsync(admin.Id, Totp.Code(setup.SharedKey), ClientContext.Unknown, None)))!;
            var challenge = (await context.LoginRawAsync(admin.Email, newPassword))!.Challenge!;
            await context.AuthenticationAsync(a => a.VerifyMfaAsync(new MfaVerifyRequest(challenge.Token, null, codes.Codes[0]), ClientContext.Unknown, None));
            secrets.AddRange([setup.SharedKey, challenge.Token, .. codes.Codes]);

            var device = await context.With<DeviceCredentialService, DeviceSecretResult>(s =>
                s.ProvisionAsync(new ProvisionDeviceRequest("ESP32-LOG", "Unit", [DeviceScopes.Heartbeat]), admin.Id, ClientContext.Unknown, None));
            await context.With<DeviceAuthenticationService, DeviceIdentity?>(s => s.AuthenticateAsync("ESP32-LOG", device.Secret!.Secret, ClientContext.Unknown, None));
            await context.With<DeviceAuthenticationService, DeviceIdentity?>(s => s.AuthenticateAsync("ESP32-LOG", "wrong-key", ClientContext.Unknown, None));
            secrets.Add(device.Secret!.Secret);

            var logs = context.Logs.Text;
            Assert.NotEmpty(logs);
            Assert.All(secrets, secret => Assert.DoesNotContain(secret, logs));
        }
    }
}
