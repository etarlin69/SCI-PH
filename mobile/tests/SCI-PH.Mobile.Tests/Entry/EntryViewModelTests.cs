using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Entry;
using SCI_PH.Mobile.Features.Auth;

namespace SCI_PH.Mobile.Tests.Entry;

// Un solo inicio de sesión (ADR-015): sin sesión se abre el login; con una sesión verificada, su área. No hay acceso
// por QR ni por rostro.
public class EntryViewModelTests
{
    [Fact]
    public async Task WithoutASessionSignInOpensTheLogin()
    {
        var navigator = new FakeNavigator();

        await new EntryViewModel(navigator, new ClinicalSessionService()).SignInCommand.ExecuteAsync(null);

        Assert.Equal(AuthRoutes.Login, Assert.Single(navigator.Navigations).Route);
    }

    [Fact]
    public async Task AVerifiedParamedicSessionReturnsToTheClinicalArea()
    {
        var navigator = new FakeNavigator();

        await new EntryViewModel(navigator, Session([], [ClinicalPermissions.EmergenciesOperate])).SignInCommand.ExecuteAsync(null);

        Assert.Equal(AuthRoutes.ClinicalArea, Assert.Single(navigator.Navigations).Route);
    }

    [Fact]
    public async Task AVerifiedPatientSessionReturnsToThePatientArea()
    {
        var navigator = new FakeNavigator();

        await new EntryViewModel(navigator, Session([AccountRoles.Patient], [])).SignInCommand.ExecuteAsync(null);

        Assert.Equal(AuthRoutes.PatientArea, Assert.Single(navigator.Navigations).Route);
    }

    [Fact]
    public async Task ASessionWithoutAnAreaOpensTheLogin()
    {
        var navigator = new FakeNavigator();

        await new EntryViewModel(navigator, Session(["Doctor"], ["VitalSigns.Write"])).SignInCommand.ExecuteAsync(null);

        Assert.Equal(AuthRoutes.Login, Assert.Single(navigator.Navigations).Route);
    }

    private static ClinicalSessionService Session(string[] roles, string[] permissions)
    {
        var session = new ClinicalSessionService();
        session.SetAuthentication("access-token", DateTimeOffset.UtcNow.AddMinutes(15));
        session.SetCurrentUser(new ClinicalUser(Guid.NewGuid(), "account", null, roles, permissions));
        return session;
    }
}
