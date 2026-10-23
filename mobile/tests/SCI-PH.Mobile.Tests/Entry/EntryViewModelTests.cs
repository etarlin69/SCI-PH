using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Entry;
using SCI_PH.Mobile.Features.Auth;
using SCI_PH.Mobile.Features.Patient;

namespace SCI_PH.Mobile.Tests.Entry;

public class EntryViewModelTests
{
    [Fact]
    public async Task ClinicalStaffOpensTheLogin()
    {
        var navigator = new FakeNavigator();

        await new EntryViewModel(navigator, new ClinicalSessionService()).OpenClinicalStaffCommand.ExecuteAsync(null);

        Assert.Equal(AuthRoutes.Login, Assert.Single(navigator.Navigations).Route);
    }

    [Fact]
    public async Task ClinicalStaffWithAVerifiedSessionReturnsToTheClinicalArea()
    {
        var navigator = new FakeNavigator();
        var session = new ClinicalSessionService();
        session.SetAuthentication("access-token", DateTimeOffset.UtcNow.AddMinutes(15));
        session.SetCurrentUser(new ClinicalUser(Guid.NewGuid(), "paramedic", null, [], [ClinicalPermissions.EmergenciesOperate]));

        await new EntryViewModel(navigator, session).OpenClinicalStaffCommand.ExecuteAsync(null);

        Assert.Equal(AuthRoutes.ClinicalArea, Assert.Single(navigator.Navigations).Route);
    }

    [Fact]
    public async Task PatientOpensTheQrAccess()
    {
        var navigator = new FakeNavigator();

        await new EntryViewModel(navigator, new ClinicalSessionService()).OpenPatientCommand.ExecuteAsync(null);

        Assert.Equal(PatientRoutes.QrAccess, Assert.Single(navigator.Navigations).Route);
    }
}
