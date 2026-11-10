using System.Net;
using System.Text;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Services;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Tests.Paramedic;

// Paciente NN con el traslado ya solicitado o en curso: la identificación sigue disponible desde el traslado, sobre la MISMA
// emergencia, sin tocar el traslado; tras la recepción del hospital deja de ofrecerse.
public class TransferIdentificationTests
{
    private const string NnCode = "NN-0123456789ABCDEF0123456789ABCDEF";

    private readonly ScriptedParamedicApi _api = new();
    private readonly FakeNavigator _navigator = new();
    private readonly Guid _emergencyId = Guid.NewGuid();
    private readonly Guid _transferId = Guid.NewGuid();

    private EmergencyDto OnServer(
        TransferStatus? transfer = TransferStatus.Requested,
        string? patient = null,
        EmergencyStatus status = EmergencyStatus.Open,
        Guid? id = null)
    {
        var emergency = new EmergencyDto
        {
            Id = id ?? _emergencyId,
            TemporaryPatientCode = NnCode,
            Status = status,
            Patient = patient is null ? null : new PatientSummaryDto { Id = Guid.NewGuid(), DisplayName = patient },
            ActiveTransfer = transfer is null ? null : new TransferDto
            {
                Id = _transferId,
                Status = transfer.Value,
                Destination = new TransferDestinationDto { Id = Guid.NewGuid(), Name = "Hospital QA" },
                RequestedAt = DateTime.UtcNow
            }
        };
        _api.Emergencies[emergency.Id] = emergency;
        return emergency;
    }

    private TransferViewModel Transfer() =>
        new(_api, new EmptyParamedicCatalog(), _navigator) { EmergencyId = _emergencyId };

    [Fact]
    public async Task ARequestedTransferOfAnNnPatientOffersIdentification()
    {
        OnServer(TransferStatus.Requested);
        var vm = Transfer();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsRequested);
        Assert.True(vm.CanIdentifyPatient);
        Assert.True(vm.IdentifyPatientCommand.CanExecute(null));
    }

    [Fact]
    public async Task AnInTransitTransferOfAnNnPatientOffersIdentification()
    {
        OnServer(TransferStatus.InTransit, status: EmergencyStatus.Transferred);
        var vm = Transfer();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsInTransit);
        Assert.True(vm.CanIdentifyPatient);
    }

    [Theory]
    [InlineData(TransferStatus.Requested, EmergencyStatus.Open)]
    [InlineData(TransferStatus.InTransit, EmergencyStatus.Transferred)]
    public async Task AnIdentifiedPatientIsNotOfferedIdentificationAgain(TransferStatus transfer, EmergencyStatus status)
    {
        OnServer(transfer, patient: "Ana Pérez", status: status);
        var vm = Transfer();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasTransfer);
        Assert.False(vm.CanIdentifyPatient);
        Assert.False(vm.IdentifyPatientCommand.CanExecute(null));
    }

    [Fact]
    public async Task TheActionOpensTheIdentificationOfTheSameEmergencyAndReturnsToTheTransfer()
    {
        OnServer(TransferStatus.InTransit, status: EmergencyStatus.Transferred);
        var vm = Transfer();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.IdentifyPatientCommand.ExecuteAsync(null);

        var (route, parameters) = Assert.Single(_navigator.Navigations);
        Assert.Equal(ParamedicRoutes.IdentifyPatient, route);
        Assert.Equal(_emergencyId, parameters!["EmergencyId"]);
        Assert.Equal(true, parameters[ParamedicRoutes.ReturnToTransfer]);
        Assert.True(vm.IsInTransit);
    }

    // Identificado durante el traslado: vuelve al MISMO traslado (sin reiniciarlo ni crear otro; la API de traslados de
    // ScriptedParamedicApi falla si se llama), con el nombre como principal y el NN como trazabilidad.
    [Theory]
    [InlineData(TransferStatus.Requested, EmergencyStatus.Open)]
    [InlineData(TransferStatus.InTransit, EmergencyStatus.Transferred)]
    public async Task AfterIdentifyingTheSameTransferContinues(TransferStatus transferStatus, EmergencyStatus status)
    {
        OnServer(transferStatus, status: status);
        var transfer = Transfer();
        await transfer.LoadCommand.ExecuteAsync(null);
        await transfer.IdentifyPatientCommand.ExecuteAsync(null);

        var identify = new IdentifyPatientViewModel(_api, new CountingFingerprintService(), _navigator)
        {
            EmergencyId = (Guid)_navigator.Navigations[0].Parameters!["EmergencyId"],
            ReturnsToTransfer = (bool)_navigator.Navigations[0].Parameters![ParamedicRoutes.ReturnToTransfer]
        };
        Assert.Equal("Volver al traslado", identify.ContinueText);
        OnServer(transferStatus, patient: "Ana Pérez", status: status);
        identify.OpenDocumentFormCommand.Execute(null);
        identify.SelectedDocumentType = "Cédula";
        identify.DocumentNumber = "001-1234567-8";
        await identify.SubmitDocumentCommand.ExecuteAsync(null);
        Assert.True(identify.IsIdentified);

        await identify.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(ParamedicRoutes.Back, _navigator.Navigations[^1].Route);

        // The transfer page appears again and reads the emergency from the server.
        await transfer.LoadCommand.ExecuteAsync(null);

        Assert.Equal((_transferId, transferStatus), (transfer.Transfer!.Id, transfer.Transfer.Status));
        Assert.Equal(("Ana Pérez", (string?)NnCode, true), (transfer.Identity!.Primary, transfer.Identity.Secondary, transfer.Identity.IsIdentified));
        Assert.False(transfer.CanIdentifyPatient);
        Assert.Equal([_emergencyId], _api.Reads.Distinct());
    }

    [Fact]
    public async Task ContinuingWithoutIdentifyingAlsoReturnsToTheTransfer()
    {
        OnServer();
        var identify = new IdentifyPatientViewModel(_api, new CountingFingerprintService(), _navigator)
        {
            EmergencyId = _emergencyId,
            ReturnsToTransfer = true
        };

        await identify.ContinueWithoutIdentifyingCommand.ExecuteAsync(null);

        Assert.Equal(ParamedicRoutes.Back, Assert.Single(_navigator.Navigations).Route);
        Assert.Equal("Volver al traslado sin identificar", identify.ContinueWithoutIdentifyingText);
    }

    [Fact]
    public async Task OutsideTheTransferTheIdentificationStillAdvancesToVitalSigns()
    {
        OnServer(transfer: null);
        var identify = new IdentifyPatientViewModel(_api, new CountingFingerprintService(), _navigator) { EmergencyId = _emergencyId };

        await identify.ContinueWithoutIdentifyingCommand.ExecuteAsync(null);

        Assert.Equal(ParamedicRoutes.VitalSigns, Assert.Single(_navigator.Navigations).Route);
        Assert.Equal("Continuar a signos vitales", identify.ContinueText);
    }

    // Recibida por el hospital: el paramédico ya no opera la emergencia (el servidor responde 409/403 a cualquier
    // identificación); la app no la ofrece.
    [Fact]
    public async Task AfterTheHandoffIdentificationIsNotOffered()
    {
        OnServer(transfer: null, status: EmergencyStatus.HandedOff);
        var vm = Transfer();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.CanIdentifyPatient);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ARejectedReadHidesAnActionShownBefore(HttpStatusCode status)
    {
        OnServer();
        var vm = Transfer();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.CanIdentifyPatient);
        _api.NextReadFailure = ScriptedParamedicApi.Status(status);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.CanIdentifyPatient);
        Assert.False(vm.IdentifyPatientCommand.CanExecute(null));
    }

    // Una lectura tardía de OTRA emergencia (la pantalla ya muestra otra, por ejemplo tras cambiar de cuenta) nunca expone
    // su paciente, su traslado ni la acción.
    [Fact]
    public async Task ALateReadOfAPreviousEmergencyIsDiscarded()
    {
        var previous = Guid.NewGuid();
        var vm = new TransferViewModel(_api, new EmptyParamedicCatalog(), _navigator) { EmergencyId = previous };
        _api.HoldReads = true;
        var load = vm.LoadCommand.ExecuteAsync(null);
        var lateRead = _api.NextPendingRead();

        vm.EmergencyId = _emergencyId;
        lateRead.SetResult(OnServer(TransferStatus.InTransit, status: EmergencyStatus.Transferred, id: previous));
        await load;

        Assert.Null(vm.Identity);
        Assert.Null(vm.Transfer);
        Assert.False(vm.CanIdentifyPatient);
    }

    [Fact]
    public async Task AnOlderReadNeverOverwritesANewerOne()
    {
        var vm = Transfer();
        _api.HoldReads = true;
        var older = vm.LoadCommand.ExecuteAsync(null);
        var olderRead = _api.NextPendingRead();
        var newer = vm.LoadCommand.ExecuteAsync(null);
        _api.NextPendingRead().SetResult(OnServer(TransferStatus.InTransit, patient: "Ana Pérez", status: EmergencyStatus.Transferred));
        await newer;

        olderRead.SetResult(new EmergencyDto
        {
            Id = _emergencyId,
            TemporaryPatientCode = NnCode,
            Status = EmergencyStatus.Open,
            ActiveTransfer = new TransferDto { Id = _transferId, Status = TransferStatus.Requested }
        });
        await older;

        Assert.Equal(("Ana Pérez", TransferStatus.InTransit, false), (vm.Identity!.Primary, vm.Transfer!.Status, vm.CanIdentifyPatient));
        Assert.False(vm.IsBusy);
    }
}

// Estado del servidor → estado de la app: la recepción del hospital no se confunde con una emergencia abierta.
public class EmergencyStatusContractTests
{
    private sealed class JsonBff(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    [Theory]
    [InlineData("Created", EmergencyStatus.Open)]
    [InlineData("InProgress", EmergencyStatus.Open)]
    [InlineData("Transferred", EmergencyStatus.Transferred)]
    [InlineData("Received", EmergencyStatus.HandedOff)]
    [InlineData("InEvolution", EmergencyStatus.HandedOff)]
    [InlineData("Closed", EmergencyStatus.Closed)]
    [InlineData("Cancelled", EmergencyStatus.Closed)]
    public async Task TheServerStatusIsMapped(string server, EmergencyStatus expected)
    {
        var id = Guid.NewGuid();
        var api = new HttpParamedicApiService(
            new MobileApiClient(new HttpClient(new JsonBff(
                $"{{\"id\":\"{id}\",\"temporaryPatientCode\":\"NN-0123456789ABCDEF0123456789ABCDEF\",\"status\":\"{server}\"," +
                "\"startedAtUtc\":\"2026-11-09T12:00:00+00:00\",\"chiefComplaint\":null,\"patient\":null,\"activeTransfer\":null}"))
            { BaseAddress = new Uri("http://sciph.test/") }),
            new MockParamedicApiService());

        var emergency = await api.GetEmergencyAsync(id);

        Assert.Equal(expected, emergency!.Status);
    }
}

// Recibida por el hospital (HandedOff): el traslado queda en solo lectura, sin formulario, destino ni acciones. Es una guía
// de la pantalla: el servidor sigue rechazando (409) cualquier paso tras la recepción.
public class TransferHandoffTests
{
    private sealed class CountingCatalog : SCI_PH.Mobile.Features.Paramedic.Services.Interfaces.IParamedicCatalogService
    {
        public int DestinationCalls { get; private set; }

        public Task<IReadOnlyList<CatalogItemDto>> GetProceduresAsync() => Task.FromResult<IReadOnlyList<CatalogItemDto>>([]);

        public Task<IReadOnlyList<MedicationCatalogItemDto>> GetMedicationsAsync() =>
            Task.FromResult<IReadOnlyList<MedicationCatalogItemDto>>([]);

        public Task<IReadOnlyList<TransferDestinationDto>> GetTransferDestinationsAsync()
        {
            DestinationCalls++;
            return Task.FromResult<IReadOnlyList<TransferDestinationDto>>([new TransferDestinationDto { Id = Guid.NewGuid(), Name = "Hospital QA" }]);
        }
    }

    private readonly ScriptedParamedicApi _api = new();
    private readonly CountingCatalog _catalog = new();
    private readonly FakeNavigator _navigator = new();
    private readonly Guid _emergencyId = Guid.NewGuid();

    private EmergencyDto State(EmergencyStatus status, TransferStatus? transfer = null, string? patient = null) => new()
    {
        Id = _emergencyId,
        TemporaryPatientCode = "NN-0123456789ABCDEF0123456789ABCDEF",
        Status = status,
        Patient = patient is null ? null : new PatientSummaryDto { Id = Guid.NewGuid(), DisplayName = patient },
        ActiveTransfer = transfer is null ? null : new TransferDto
        {
            Id = Guid.NewGuid(),
            Status = transfer.Value,
            Destination = new TransferDestinationDto { Id = Guid.NewGuid(), Name = "Hospital QA" },
            RequestedAt = DateTime.UtcNow
        }
    };

    private EmergencyDto OnServer(EmergencyStatus status, TransferStatus? transfer = null, string? patient = null) =>
        _api.Emergencies[_emergencyId] = State(status, transfer, patient);

    private TransferViewModel Screen() => new(_api, _catalog, _navigator) { EmergencyId = _emergencyId };

    private static void AssertReadOnlyCompletion(TransferViewModel vm)
    {
        Assert.True(vm.IsHandedOff);
        Assert.Equal(ParamedicMessages.TransferHandedOff, vm.HandedOffMessage);
        Assert.False(vm.ShowRequestForm);
        Assert.False(vm.ShowInstructions);
        Assert.False(vm.HasTransfer);
        Assert.False(vm.IsInTransit);
        Assert.Empty(vm.Destinations);
        vm.SelectedDestination = new TransferDestinationDto { Id = Guid.NewGuid(), Name = "Hospital QA" };
        Assert.False(vm.RequestCommand.CanExecute(null));
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.False(vm.CancelCommand.CanExecute(null));
        Assert.False(vm.IdentifyPatientCommand.CanExecute(null));
    }

    // NN o identificado: después de la recepción la regla es la misma.
    [Theory]
    [InlineData(null)]
    [InlineData("Ana Pérez")]
    public async Task AHandedOffCaseShowsOnlyTheCompletionMessage(string? patient)
    {
        OnServer(EmergencyStatus.HandedOff, patient: patient);
        var vm = Screen();

        await vm.LoadCommand.ExecuteAsync(null);

        AssertReadOnlyCompletion(vm);
        Assert.Equal(0, _catalog.DestinationCalls);
    }

    // Recibida mientras el paramédico tenía abierta la pantalla: al volver a leer desaparece el traslado y sus acciones.
    [Fact]
    public async Task AReceptionWhileTheScreenIsOpenRemovesTheTransferActions()
    {
        OnServer(EmergencyStatus.Transferred, TransferStatus.InTransit);
        var vm = Screen();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.CancelCommand.CanExecute(null));

        OnServer(EmergencyStatus.HandedOff);
        await vm.LoadCommand.ExecuteAsync(null);

        AssertReadOnlyCompletion(vm);
    }

    [Fact]
    public async Task AnOpenCaseKeepsTheTransferRequest()
    {
        OnServer(EmergencyStatus.Open);
        var vm = Screen();

        await vm.LoadCommand.ExecuteAsync(null);
        vm.SelectedDestination = vm.Destinations.Single();

        Assert.False(vm.IsHandedOff);
        Assert.True(vm.ShowRequestForm);
        Assert.True(vm.ShowInstructions);
        Assert.Equal(1, _catalog.DestinationCalls);
        Assert.True(vm.RequestCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(TransferStatus.Requested, EmergencyStatus.Open)]
    [InlineData(TransferStatus.InTransit, EmergencyStatus.Transferred)]
    public async Task AnActiveTransferKeepsItsActions(TransferStatus transfer, EmergencyStatus status)
    {
        OnServer(status, transfer);
        var vm = Screen();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.IsHandedOff);
        Assert.False(vm.ShowRequestForm);
        Assert.True(vm.HasTransfer);
        Assert.Equal(transfer == TransferStatus.Requested, vm.StartCommand.CanExecute(null));
        Assert.True(vm.CancelCommand.CanExecute(null));
    }

    // Una lectura anterior (aún Open, sin traslado) que llega después de la que ya dice HandedOff no devuelve el formulario.
    [Fact]
    public async Task ALateOpenReadNeverRestoresTheActionsAfterTheHandoff()
    {
        var vm = Screen();
        _api.HoldReads = true;
        var older = vm.LoadCommand.ExecuteAsync(null);
        var olderRead = _api.NextPendingRead();
        var newer = vm.LoadCommand.ExecuteAsync(null);
        _api.NextPendingRead().SetResult(State(EmergencyStatus.HandedOff));
        await newer;

        olderRead.SetResult(State(EmergencyStatus.Open));
        await older;

        AssertReadOnlyCompletion(vm);
        Assert.Equal(0, _catalog.DestinationCalls);
        Assert.False(vm.IsBusy);
    }
}
