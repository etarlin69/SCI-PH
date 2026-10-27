using System.Net;
using System.Text.Json;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Services;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Tests.Paramedic;

// Traslado REAL de la emergencia: destinos del servidor, solicitud idempotente por clave, inicio y cancelación sobre un
// traslado concreto, estado restaurado desde el activeTransfer de la emergencia, y documentación durante el traslado.
// La recepción la hace el hospital: la app no la ofrece.
public class TransferTests
{
    private readonly FakeTransferBff _bff = new();
    private readonly MockParamedicApiService _mock = new();
    private readonly FakeNavigator _navigator = new();
    private readonly Guid _emergencyId = Guid.NewGuid();

    public TransferTests() => _mock.Adopt(new EmergencyDto { Id = _emergencyId });

    private HttpParamedicApiService Service() => new(_bff.Client(), _mock);

    private TransferViewModel ViewModel() => new(Service(), new HttpParamedicCatalogService(_bff.Client()), _navigator) { EmergencyId = _emergencyId };

    private async Task<TransferViewModel> LoadedAsync()
    {
        var vm = ViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        return vm;
    }

    private async Task<TransferViewModel> RequestedAsync()
    {
        var vm = await LoadedAsync();
        vm.SelectedDestination = vm.Destinations.Single(d => d.Id == FakeTransferBff.Central);
        await vm.RequestCommand.ExecuteAsync(null);
        return vm;
    }

    private JsonElement LastBody() => JsonDocument.Parse(_bff.Bodies[^1]).RootElement;

    [Fact]
    public async Task TheDestinationsComeFromTheServerWithTypeAndPlace()
    {
        var vm = await LoadedAsync();

        Assert.Contains("GET /api/mobile/catalogs/transfer-destinations", _bff.Paths);
        Assert.Equal(["Hospital Central · Hospital · Distrito Nacional, Santo Domingo", "Centro Norte · Centro de emergencias · Santiago, Santiago"],
            vm.Destinations.Select(d => d.DisplayName));
        Assert.True(vm.HasNoTransfer);
        Assert.False(vm.RequestCommand.CanExecute(null));
    }

    [Fact]
    public async Task TheRequestSendsTheExactContractAndShowsTheRequestedTransfer()
    {
        var vm = await LoadedAsync();
        vm.SelectedDestination = vm.Destinations[0];
        vm.Reason = "  Trauma mayor ";

        await vm.RequestCommand.ExecuteAsync(null);

        var body = LastBody();
        Assert.Equal(["clientRequestId", "destinationClinicId", "reason"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal((FakeTransferBff.Central, "Trauma mayor"), (body.GetProperty("destinationClinicId").GetGuid(), body.GetProperty("reason").GetString()));
        foreach (var field in new[] { "originClinicId", "requestedAtUtc", "status", "staffId", "ambulanceId" })
        {
            Assert.False(body.TryGetProperty(field, out _), field);
        }

        Assert.True(vm.IsRequested);
        Assert.False(vm.IsInTransit);
        Assert.StartsWith("Traslado solicitado a las", vm.StatusText);
        Assert.Contains("Hospital Central", vm.DestinationText);
        Assert.True(vm.StartCommand.CanExecute(null));
        Assert.True(vm.CancelCommand.CanExecute(null));
    }

    // A lost answer: the retry with the same key is the same transfer (200), never a second one.
    [Fact]
    public async Task ALostRequestIsRetriedWithTheSameKey()
    {
        var vm = await LoadedAsync();
        vm.SelectedDestination = vm.Destinations[0];
        _bff.LostResponsesLeft = 1;

        await vm.RequestCommand.ExecuteAsync(null);
        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);
        Assert.True(vm.HasNoTransfer);
        var firstKey = LastBody().GetProperty("clientRequestId").GetGuid();

        await vm.RequestCommand.ExecuteAsync(null);

        Assert.Equal(firstKey, LastBody().GetProperty("clientRequestId").GetGuid());
        Assert.Equal(1, _bff.Requested);
        Assert.True(vm.IsRequested);
    }

    [Fact]
    public async Task StartingPutsThePatientInTransitAndARetryIsSafe()
    {
        var vm = await RequestedAsync();

        await vm.StartCommand.ExecuteAsync(null);

        Assert.True(vm.IsInTransit);
        Assert.Equal("Paciente en traslado hacia Hospital Central", vm.DestinationText);
        Assert.StartsWith("En traslado desde las", vm.StatusText);
        var transferId = vm.Transfer!.Id;
        Assert.Contains($"POST /api/mobile/emergencies/{_emergencyId}/transfers/{transferId}/start", _bff.Paths);

        // A start whose answer was lost: the transfer is already InTransit on the server, the retry answers 200 and a
        // reopened screen shows it in transit.
        Assert.Equal(TransferStatus.InTransit, (await Service().StartTransferAsync(_emergencyId, transferId)).Status);
        Assert.Equal("InTransit", _bff.StatusOf(transferId));
        Assert.True((await LoadedAsync()).IsInTransit);
    }

    [Fact]
    public async Task CancellingARequestedTransferAllowsAnotherDestination()
    {
        var vm = await RequestedAsync();
        var first = vm.Transfer!.Id;

        await vm.CancelCommand.ExecuteAsync(null);

        Assert.Equal("Cancelled", _bff.StatusOf(first));
        Assert.True(vm.HasNoTransfer);
        Assert.Equal(ParamedicMessages.TransferCancelled, vm.SuccessMessage);
        Assert.Contains($"POST /api/mobile/emergencies/{_emergencyId}/transfers/{first}/cancel", _bff.Paths);

        vm.SelectedDestination = vm.Destinations.Single(d => d.Id == FakeTransferBff.Norte);
        await vm.RequestCommand.ExecuteAsync(null);

        Assert.True(vm.IsRequested);
        Assert.NotEqual(first, vm.Transfer!.Id);
        Assert.Equal("Centro Norte", vm.Transfer.Destination.Name);
    }

    [Fact]
    public async Task CancellingAnInTransitTransferAndRepeatingTheCancelIsSafe()
    {
        var vm = await RequestedAsync();
        await vm.StartCommand.ExecuteAsync(null);
        var transferId = vm.Transfer!.Id;

        await vm.CancelCommand.ExecuteAsync(null);

        Assert.True(vm.HasNoTransfer);
        Assert.Equal("Cancelled", _bff.StatusOf(transferId));
        Assert.Equal(TransferStatus.Cancelled, (await Service().CancelTransferAsync(_emergencyId, transferId)).Status);
    }

    // The screen is rebuilt from the server's activeTransfer, not from memory.
    [Theory]
    [InlineData("Requested")]
    [InlineData("InTransit")]
    public async Task TheStateIsRestoredFromTheEmergencysActiveTransfer(string status)
    {
        var transferId = _bff.Seed(_emergencyId, status);

        var vm = await LoadedAsync();

        Assert.Equal(transferId, vm.Transfer!.Id);
        Assert.Equal((status == "Requested", status == "InTransit"), (vm.IsRequested, vm.IsInTransit));
        Assert.Contains("Hospital Central", vm.DestinationText);
        Assert.Contains($"GET /api/mobile/emergencies/{_emergencyId}", _bff.Paths);
    }

    // In transit the paramedic goes back to the four clinical screens of the same emergency.
    [Fact]
    public async Task InTransitTheParamedicKeepsDocumentingOnTheSameEmergency()
    {
        _bff.Seed(_emergencyId, "InTransit");
        var vm = await LoadedAsync();

        await vm.GoToVitalSignsCommand.ExecuteAsync(null);
        await vm.GoToProceduresCommand.ExecuteAsync(null);
        await vm.GoToMedicationsCommand.ExecuteAsync(null);
        await vm.GoToNotesCommand.ExecuteAsync(null);

        Assert.Equal([ParamedicRoutes.VitalSigns, ParamedicRoutes.Procedures, ParamedicRoutes.Medications, ParamedicRoutes.Notes],
            _navigator.Navigations.Select(n => n.Route));
        Assert.All(_navigator.Navigations, n => Assert.Equal(_emergencyId, n.Parameters!["EmergencyId"]));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, MobileApiErrorMessages.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden, MobileApiErrorMessages.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ParamedicMessages.TransferNotFound)]
    [InlineData(HttpStatusCode.Conflict, ParamedicMessages.TransferConflict)]
    [InlineData(HttpStatusCode.InternalServerError, ParamedicMessages.RequestTransferFailed)]
    public async Task ARejectedRequestShowsASafeMessageAndKeepsItsKey(HttpStatusCode status, string message)
    {
        var vm = await LoadedAsync();
        vm.SelectedDestination = vm.Destinations[0];
        _bff.NextStatus = status;

        await vm.RequestCommand.ExecuteAsync(null);

        Assert.Equal(message, vm.ErrorMessage);
        Assert.DoesNotContain("Forced", vm.ErrorMessage);
        Assert.True(vm.HasNoTransfer);
        var key = LastBody().GetProperty("clientRequestId").GetGuid();
        await vm.RequestCommand.ExecuteAsync(null);
        Assert.Equal(key, LastBody().GetProperty("clientRequestId").GetGuid());
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, ParamedicMessages.TransferConflict)]
    [InlineData(HttpStatusCode.NotFound, ParamedicMessages.TransferNotFound)]
    [InlineData(HttpStatusCode.Forbidden, MobileApiErrorMessages.Forbidden)]
    public async Task ARejectedStartKeepsTheRequestedTransfer(HttpStatusCode status, string message)
    {
        var vm = await RequestedAsync();
        _bff.NextStatus = status;

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal(message, vm.ErrorMessage);
        Assert.True(vm.IsRequested);
    }

    [Fact]
    public async Task ANetworkFailureOnStartKeepsTheScreenAndTheRetryStarts()
    {
        var vm = await RequestedAsync();
        _bff.UnreachableLeft = 1;

        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);
        Assert.True(vm.IsRequested);

        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsInTransit);
    }

    [Fact]
    public void ThereIsNoReceiveControlInTheApp()
    {
        Assert.DoesNotContain(typeof(TransferViewModel).GetProperties(), p => p.Name.Contains("Receive", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(HttpParamedicApiService).GetMethods(), m => m.Name.Contains("Receive", StringComparison.Ordinal));
    }
}
