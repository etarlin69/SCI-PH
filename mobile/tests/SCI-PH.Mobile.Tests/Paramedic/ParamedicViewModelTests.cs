using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Tests.Paramedic;

// Los ViewModels se prueban con servicios falsos: sin Shell, ventana, handlers ni UI.
public class ParamedicViewModelTests
{
    [Fact]
    public async Task ARetriedEmergencyKeepsItsRequestIdAndTheNextOneGetsANewOne()
    {
        var api = new RecordingParamedicApi { TransientFailuresLeft = 1 };
        var navigator = new FakeNavigator();
        var vm = new NewEmergencyViewModel(api, navigator) { ChiefComplaint = "Dolor torácico" };

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.Empty(navigator.Navigations);
        Assert.Equal("Dolor torácico", vm.ChiefComplaint);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(vm.HasError);
        var (failed, retried) = (api.CreateRequests[0], api.CreateRequests[1]);
        Assert.Equal(failed.ClientRequestId, retried.ClientRequestId);
        Assert.Equal(failed.StartedAt, retried.StartedAt);
        Assert.Equal(NewEmergencyViewModel.IdentifyAfterCreate, Assert.Single(navigator.Navigations).Route);

        vm.ChiefComplaint = "Caída de altura";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotEqual(retried.ClientRequestId, api.CreateRequests[2].ClientRequestId);
    }

    [Fact]
    public async Task AnEmergencyCanContinueAsUnidentifiedWithoutReadingTheFingerprint()
    {
        var api = new RecordingParamedicApi();
        var fingerprint = new CountingFingerprintService();
        var navigator = new FakeNavigator();
        var emergencyId = Guid.NewGuid();
        var vm = new IdentifyPatientViewModel(api, fingerprint, navigator) { EmergencyId = emergencyId };

        // Las tres vías se ofrecen desde el inicio; no hay lectura de huella ni aviso hasta pedirla.
        Assert.True(vm.CanIdentify);
        Assert.False(vm.ShowFingerprintPanel);
        Assert.False(vm.HasFingerprintMessage);
        Assert.True(vm.ContinueWithoutIdentifyingCommand.CanExecute(null));

        await vm.ContinueWithoutIdentifyingCommand.ExecuteAsync(null);

        Assert.Equal(0, fingerprint.Calls);
        Assert.Equal(0, api.IdentifyByDocumentCalls);
        var (route, parameters) = Assert.Single(navigator.Navigations);
        Assert.Equal(ParamedicRoutes.VitalSigns, route);
        Assert.Equal(emergencyId, parameters!["EmergencyId"]);
    }

    [Fact]
    public void TheDocumentFormOpensWithoutReadingTheFingerprint()
    {
        var fingerprint = new CountingFingerprintService();
        var vm = new IdentifyPatientViewModel(new RecordingParamedicApi(), fingerprint, new FakeNavigator());

        vm.OpenDocumentFormCommand.Execute(null);

        Assert.True(vm.ShowDocumentForm);
        Assert.Equal(0, fingerprint.Calls);
    }

    [Fact]
    public async Task TheParamedicStepsNavigateThroughTheNavigatorWithTheEmergency()
    {
        var api = new RecordingParamedicApi();
        var catalog = new EmptyParamedicCatalog();
        var navigator = new FakeNavigator();
        var dialogs = new FakeDialogs();
        var emergencyId = Guid.NewGuid();

        await new VitalSignsViewModel(api, navigator) { EmergencyId = emergencyId }.ContinueCommand.ExecuteAsync(null);
        await new ProceduresViewModel(api, catalog, navigator) { EmergencyId = emergencyId }.ContinueCommand.ExecuteAsync(null);
        await new MedicationsViewModel(api, catalog, navigator) { EmergencyId = emergencyId }.ContinueCommand.ExecuteAsync(null);
        await new PrehospitalNotesViewModel(api, navigator) { EmergencyId = emergencyId }.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(
            [ParamedicRoutes.Procedures, ParamedicRoutes.Medications, ParamedicRoutes.Notes, ParamedicRoutes.Transfer],
            navigator.Navigations.Select(n => n.Route));
        Assert.All(navigator.Navigations, n => Assert.Equal(emergencyId, n.Parameters!["EmergencyId"]));
        Assert.Empty(dialogs.Shown);
    }
}
