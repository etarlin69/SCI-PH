using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.Startup;
using SCI_PH.Mobile.Features.Auth;

namespace SCI_PH.Mobile.Tests.Auth;

// Arranque con MobileAuthService real: el área paramédica solo se abre tras refresh + /me + permiso.
public class StartupTests
{
    private readonly FakeNavigator _navigator = new();

    private async Task<string> StartAsync(AuthHarness h)
    {
        await new StartupViewModel(h.Auth, _navigator).StartAsync();
        return Assert.Single(_navigator.Navigations).Route;
    }

    [Fact]
    public async Task WithoutAStoredRefreshTokenTheAppOpensTheEntry()
    {
        using var h = new AuthHarness();

        Assert.Equal(AppRoutes.Root(AppRoutes.Entry), await StartAsync(h));
        Assert.Equal(0, h.Bff.RefreshCalls);
        Assert.False(h.Session.IsAuthenticated);
    }

    [Fact]
    public async Task AValidStoredSessionIsRefreshedVerifiedWithMeAndOpensTheClinicalArea()
    {
        using var h = new AuthHarness();
        var (_, refreshToken) = h.Bff.IssueSession();
        await h.Tokens.SaveRefreshTokenAsync(refreshToken);

        Assert.Equal(AuthRoutes.ClinicalArea, await StartAsync(h));
        Assert.Equal(1, h.Bff.RefreshCalls);
        Assert.Equal(1, h.Bff.MeCalls);
        Assert.True(h.Session.CanOperateEmergencies);
        Assert.Equal(h.Bff.AccessToken, h.Session.AccessToken);
        Assert.Equal(h.Bff.RefreshToken, Assert.Single(h.Storage.Values).Value);
        Assert.NotEqual(refreshToken, h.Bff.RefreshToken);
    }

    [Fact]
    public async Task AnExpiredOrRevokedStoredTokenIsClearedAndTheAppOpensTheEntry()
    {
        using var h = new AuthHarness();
        var (_, refreshToken) = h.Bff.IssueSession();
        await h.Tokens.SaveRefreshTokenAsync(refreshToken);
        h.Bff.RevokeSession();

        Assert.Equal(AppRoutes.Root(AppRoutes.Entry), await StartAsync(h));
        Assert.Empty(h.Storage.Values);
        Assert.False(h.Session.IsAuthenticated);
        Assert.Equal(0, h.Bff.MeCalls);
    }

    [Fact]
    public async Task AValidSessionWithoutTheEmergencyPermissionNeverOpensTheClinicalArea()
    {
        using var h = new AuthHarness();
        h.Bff.Permissions = ["VitalSigns.Write"];
        var (_, refreshToken) = h.Bff.IssueSession();
        await h.Tokens.SaveRefreshTokenAsync(refreshToken);

        Assert.Equal(AppRoutes.Root(AppRoutes.Entry), await StartAsync(h));
        Assert.False(h.Session.IsAuthenticated);
        Assert.Null(h.Session.AccessToken);
        Assert.Empty(h.Storage.Values);
        Assert.Equal(1, h.Bff.LogoutCalls);
    }

    [Fact]
    public async Task WithoutConnectionTheAppOpensTheEntryAndKeepsTheTokenForTheNextStart()
    {
        using var h = new AuthHarness();
        var (_, refreshToken) = h.Bff.IssueSession();
        await h.Tokens.SaveRefreshTokenAsync(refreshToken);
        h.Bff.UnreachablePaths.Add("/api/mobile/auth/refresh");

        Assert.Equal(AppRoutes.Root(AppRoutes.Entry), await StartAsync(h));
        Assert.False(h.Session.IsAuthenticated);
        Assert.Equal(refreshToken, Assert.Single(h.Storage.Values).Value);
    }

    [Fact]
    public async Task RestoreReportsEachOutcome()
    {
        using var h = new AuthHarness();
        Assert.Equal(ClinicalSessionRestore.NoSession, await h.Auth.RestoreSessionAsync());

        var (_, refreshToken) = h.Bff.IssueSession();
        await h.Tokens.SaveRefreshTokenAsync(refreshToken);
        Assert.Equal(ClinicalSessionRestore.Restored, await h.Auth.RestoreSessionAsync());
    }
}
