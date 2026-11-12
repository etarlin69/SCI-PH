using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Features.Auth.ViewModels;
using SCI_PH.Mobile.Common.Navigation;

namespace SCI_PH.Mobile.Tests.Auth;

// MobileAuthService real contra el BFF falso: login, MFA, área de /me (paramédica o paciente), almacenamiento y logout.
public class MobileAuthServiceTests
{
    [Fact]
    public async Task LoginWithoutMfaKeepsTheAccessTokenInMemoryAndOnlyTheRefreshTokenInSecureStorage()
    {
        using var h = new AuthHarness();

        var result = await h.Auth.LoginAsync(FakeMobileBff.Identifier, FakeMobileBff.Password);

        Assert.Equal(ClinicalSignInStatus.Authenticated, result.Status);
        Assert.Equal(h.Bff.AccessToken, h.Session.AccessToken);
        Assert.True(h.Session.IsAuthenticated);
        Assert.Equal(1, h.Bff.MeCalls);

        // SecureStorage guarda un único valor: el refresh token. Nunca el access token ni la contraseña.
        var stored = Assert.Single(h.Storage.Values).Value;
        Assert.Equal(h.Bff.RefreshToken, stored);
        Assert.DoesNotContain(h.Storage.Values.Values, value => value == h.Session.AccessToken || value == FakeMobileBff.Password);
    }

    [Fact]
    public async Task TheCurrentUserComesFromMeWithMinimalData()
    {
        using var h = new AuthHarness();

        await h.SignInAsync();

        var user = h.Session.CurrentUser!;
        Assert.Equal(FakeMobileBff.Identifier, user.UserName);
        Assert.Equal(FakeMobileBff.Identifier, user.Email);
        Assert.Contains(ClinicalPermissions.EmergenciesOperate, user.Permissions);
        Assert.True(h.Session.CanOperateEmergencies);
    }

    [Fact]
    public async Task InvalidCredentialsCreateNoSession()
    {
        using var h = new AuthHarness();

        var ex = await Assert.ThrowsAsync<MobileApiException>(() => h.Auth.LoginAsync(FakeMobileBff.Identifier, "wrong"));

        Assert.Equal(MobileApiError.Unauthorized, ex.Error);
        Assert.False(h.Session.IsAuthenticated);
        Assert.Null(h.Session.AccessToken);
        Assert.Empty(h.Storage.Values);
    }

    [Fact]
    public async Task ALoginThatRequiresMfaCreatesNoSessionUntilTheCodeIsVerified()
    {
        using var h = new AuthHarness();
        h.Bff.RequireMfa = true;

        var login = await h.Auth.LoginAsync(FakeMobileBff.Identifier, FakeMobileBff.Password);

        Assert.Equal(ClinicalSignInStatus.MfaRequired, login.Status);
        Assert.NotNull(login.MfaChallenge);
        Assert.Null(h.Session.AccessToken);
        Assert.Empty(h.Storage.Values);

        var verified = await h.Auth.VerifyMfaCodeAsync(login.MfaChallenge, FakeMobileBff.TotpCode);

        Assert.Equal(ClinicalSignInStatus.Authenticated, verified.Status);
        Assert.True(h.Session.CanOperateEmergencies);
        var body = Assert.Single(h.Bff.MfaVerifyBodies);
        Assert.Equal(login.MfaChallenge.Token, body.GetProperty("challengeToken").GetString());
        Assert.Equal(FakeMobileBff.TotpCode, body.GetProperty("code").GetString());
        Assert.False(body.TryGetProperty("recoveryCode", out var recovery) && recovery.ValueKind == System.Text.Json.JsonValueKind.String);
    }

    [Fact]
    public async Task ARecoveryCodeIsSentInsteadOfATotp()
    {
        using var h = new AuthHarness();
        h.Bff.RequireMfa = true;
        var login = await h.Auth.LoginAsync(FakeMobileBff.Identifier, FakeMobileBff.Password);

        var verified = await h.Auth.VerifyRecoveryCodeAsync(login.MfaChallenge!, FakeMobileBff.RecoveryCode);

        Assert.Equal(ClinicalSignInStatus.Authenticated, verified.Status);
        var body = Assert.Single(h.Bff.MfaVerifyBodies);
        Assert.Equal(FakeMobileBff.RecoveryCode, body.GetProperty("recoveryCode").GetString());
        Assert.False(body.TryGetProperty("code", out var code) && code.ValueKind == System.Text.Json.JsonValueKind.String);
    }

    [Fact]
    public async Task AFailedMfaChallengeCreatesNoSession()
    {
        using var h = new AuthHarness();
        h.Bff.RequireMfa = true;
        var login = await h.Auth.LoginAsync(FakeMobileBff.Identifier, FakeMobileBff.Password);

        var ex = await Assert.ThrowsAsync<MobileApiException>(() => h.Auth.VerifyMfaCodeAsync(login.MfaChallenge!, "000000"));

        Assert.Equal(MobileApiError.Unauthorized, ex.Error);
        Assert.Null(h.Session.AccessToken);
        Assert.False(h.Session.IsAuthenticated);
        Assert.Empty(h.Storage.Values);
    }

    [Fact]
    public async Task WithTheEmergencyPermissionTheParamedicAreaIsAllowedWhateverTheRole()
    {
        using var h = new AuthHarness();
        h.Bff.Roles = ["Doctor"];
        h.Bff.Permissions = [ClinicalPermissions.EmergenciesOperate];

        var result = await h.Auth.LoginAsync(FakeMobileBff.Identifier, FakeMobileBff.Password);

        Assert.Equal(ClinicalSignInStatus.Authenticated, result.Status);
        Assert.True(h.Session.CanOperateEmergencies);
    }

    [Fact]
    public async Task WithoutTheEmergencyPermissionTheSessionIsRevokedAndDiscardedEvenForTheParamedicRole()
    {
        using var h = new AuthHarness();
        h.Bff.Roles = ["Paramedic"];
        h.Bff.Permissions = ["VitalSigns.Write", "Encounters.Create"];

        var result = await h.Auth.LoginAsync(FakeMobileBff.Identifier, FakeMobileBff.Password);

        Assert.Equal(ClinicalSignInStatus.NoAccess, result.Status);
        Assert.False(h.Session.CanOperateEmergencies);
        Assert.Null(h.Session.AccessToken);
        Assert.Empty(h.Storage.Values);
        Assert.Equal(1, h.Bff.LogoutCalls);
        Assert.Null(h.Bff.RefreshToken);
    }

    [Fact]
    public async Task IfMeIsUnreachableAfterLoginNothingIsLeftHalfDone()
    {
        using var h = new AuthHarness();
        h.Bff.UnreachablePaths.Add("/api/mobile/auth/me");

        var ex = await Assert.ThrowsAsync<MobileApiException>(() => h.Auth.LoginAsync(FakeMobileBff.Identifier, FakeMobileBff.Password));

        Assert.Equal(MobileApiError.Network, ex.Error);
        Assert.Null(h.Session.AccessToken);
        Assert.Empty(h.Storage.Values);
    }

    [Fact]
    public async Task LogoutRevokesOnTheServerAndClearsTheLocalSession()
    {
        using var h = new AuthHarness();
        await h.SignInAsync();

        await h.Auth.LogoutAsync();

        Assert.Equal(1, h.Bff.LogoutCalls);
        Assert.Null(h.Bff.RefreshToken);
        Assert.Null(h.Session.AccessToken);
        Assert.Null(h.Session.CurrentUser);
        Assert.Empty(h.Storage.Values);
        Assert.Equal(0, h.SessionExpiredCount);
    }

    [Fact]
    public async Task LogoutClearsTheLocalSessionEvenWhenTheServerIsUnreachable()
    {
        using var h = new AuthHarness();
        await h.SignInAsync();
        h.Bff.UnreachablePaths.Add("/api/mobile/auth/logout");

        await h.Auth.LogoutAsync();

        Assert.Null(h.Session.AccessToken);
        Assert.False(h.Session.IsAuthenticated);
        Assert.Empty(h.Storage.Values);
    }

    [Fact]
    public async Task APatientAccountSignsInToThePatientAreaWithoutAnyPermission()
    {
        using var h = new AuthHarness();
        h.Bff.Roles = [AccountRoles.Patient];
        h.Bff.Permissions = [];

        var result = await h.Auth.LoginAsync(FakeMobileBff.Identifier, FakeMobileBff.Password);

        Assert.Equal((ClinicalSignInStatus.Authenticated, AccountArea.Patient), (result.Status, result.Area));
        Assert.Equal(AccountArea.Patient, h.Session.Area);
        Assert.False(h.Session.CanOperateEmergencies);
        Assert.Equal(h.Bff.RefreshToken, Assert.Single(h.Storage.Values).Value);
    }

    [Fact]
    public async Task AParamedicSignsInToTheParamedicArea()
    {
        using var h = new AuthHarness();

        var result = await h.Auth.LoginAsync(FakeMobileBff.Identifier, FakeMobileBff.Password);

        Assert.Equal((ClinicalSignInStatus.Authenticated, AccountArea.Paramedic), (result.Status, result.Area));
        Assert.Equal(AccountArea.Paramedic, h.Session.Area);
    }

    [Fact]
    public async Task AnAccountThatWouldBeBothPatientAndParamedicGetsNoArea()
    {
        using var h = new AuthHarness();
        h.Bff.Roles = [AccountRoles.Patient];
        h.Bff.Permissions = [ClinicalPermissions.EmergenciesOperate];

        var result = await h.Auth.LoginAsync(FakeMobileBff.Identifier, FakeMobileBff.Password);

        Assert.Equal(ClinicalSignInStatus.NoAccess, result.Status);
        Assert.Equal(AccountArea.None, h.Session.Area);
        Assert.Empty(h.Storage.Values);
        Assert.Equal(1, h.Bff.LogoutCalls);
    }

    [Fact]
    public async Task APatientSessionIsRestoredRefreshedAndLoggedOutLikeAnyOther()
    {
        using var h = new AuthHarness();
        h.Bff.Roles = [AccountRoles.Patient];
        h.Bff.Permissions = [];
        var (_, refreshToken) = h.Bff.IssueSession();
        await h.Tokens.SaveRefreshTokenAsync(refreshToken);

        Assert.Equal(ClinicalSessionRestore.Restored, await h.Auth.RestoreSessionAsync());
        Assert.Equal(AccountArea.Patient, h.Session.Area);

        h.Bff.ExpireAccessToken();
        await h.GetProtectedAsync();
        Assert.Equal(2, h.Bff.RefreshCalls);

        await h.Auth.LogoutAsync();
        Assert.Equal(AccountArea.None, h.Session.Area);
        Assert.Empty(h.Storage.Values);
        Assert.Equal(1, h.Bff.LogoutCalls);
    }

    [Fact]
    public async Task ClinicalLogoutFromTheParamedicAreaGoesToTheEntryEvenOffline()
    {
        using var h = new AuthHarness();
        await h.SignInAsync();
        h.Bff.UnreachablePaths.Add("/api/mobile/auth/logout");
        var navigator = new FakeNavigator();

        await new ClinicalAccountViewModel(h.Auth, h.Session, navigator).LogoutCommand.ExecuteAsync(null);

        Assert.Empty(h.Storage.Values);
        Assert.Equal(AppRoutes.Root(AppRoutes.Entry), Assert.Single(navigator.Navigations).Route);
    }
}
