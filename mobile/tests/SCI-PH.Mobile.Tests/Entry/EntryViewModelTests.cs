using SCI_PH.Mobile.Common.Entry;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Patient;

namespace SCI_PH.Mobile.Tests.Entry;

public class EntryViewModelTests
{
    [Fact]
    public async Task ClinicalStaffOpensTheParamedicFlow()
    {
        var navigator = new FakeNavigator();

        await new EntryViewModel(navigator).OpenClinicalStaffCommand.ExecuteAsync(null);

        Assert.Equal(ParamedicRoutes.NewEmergency, Assert.Single(navigator.Navigations).Route);
    }

    [Fact]
    public async Task PatientOpensTheQrAccess()
    {
        var navigator = new FakeNavigator();

        await new EntryViewModel(navigator).OpenPatientCommand.ExecuteAsync(null);

        Assert.Equal(PatientRoutes.QrAccess, Assert.Single(navigator.Navigations).Route);
    }
}
