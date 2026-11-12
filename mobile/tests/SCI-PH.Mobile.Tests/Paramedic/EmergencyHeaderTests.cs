using System.Net;
using System.Text;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Paramedic.Models;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Services;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Tests.Paramedic;

// Cabecera de la emergencia en las pantallas del flujo (EmergencyWorkflowViewModel): siempre desde el servidor, el NN como
// principal hasta la identificación y como trazabilidad después, y "Inicio" de vuelta al inicio del paramédico.
public class EmergencyHeaderTests
{
    private const string NnCode = "NN-0123456789ABCDEF0123456789ABCDEF";

    private readonly ScriptedParamedicApi _api = new();
    private readonly FakeNavigator _navigator = new();
    private readonly Guid _emergencyId = Guid.NewGuid();

    private EmergencyDto OnServer(string? patient = null, string? code = NnCode)
    {
        var emergency = new EmergencyDto
        {
            Id = _emergencyId,
            TemporaryPatientCode = code,
            Patient = patient is null ? null : new PatientSummaryDto { Id = Guid.NewGuid(), DisplayName = patient }
        };
        _api.Emergencies[emergency.Id] = emergency;
        return emergency;
    }

    public static TheoryData<string> Screens => new() { "Vitals", "Procedures", "Medications", "Notes", "Transfer", "Identify" };

    private EmergencyWorkflowViewModel Screen(string name) => name switch
    {
        "Vitals" => new VitalSignsViewModel(_api, _navigator) { EmergencyId = _emergencyId },
        "Procedures" => new ProceduresViewModel(_api, new EmptyParamedicCatalog(), _navigator) { EmergencyId = _emergencyId },
        "Medications" => new MedicationsViewModel(_api, new EmptyParamedicCatalog(), _navigator) { EmergencyId = _emergencyId },
        "Notes" => new PrehospitalNotesViewModel(_api, _navigator) { EmergencyId = _emergencyId },
        "Transfer" => new TransferViewModel(_api, new EmptyParamedicCatalog(), _navigator) { EmergencyId = _emergencyId },
        _ => new IdentifyPatientViewModel(_api, new CountingFingerprintService(), _navigator) { EmergencyId = _emergencyId }
    };

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task EveryScreenShowsTheNnCodeOfTheUnidentifiedCase(string name)
    {
        OnServer();
        var vm = Screen(name);
        Assert.False(vm.HasIdentity);

        await vm.RefreshIdentityAsync();

        Assert.Equal((NnCode, false, false), (vm.Identity!.Primary, vm.Identity.HasSecondary, vm.Identity.IsIdentified));
    }

    // Identified elsewhere (another screen, another method): the next read shows the name with the NN code kept.
    [Theory]
    [MemberData(nameof(Screens))]
    public async Task AfterIdentificationTheNameIsPrimaryAndTheNnCodeStays(string name)
    {
        OnServer();
        var vm = Screen(name);
        await vm.RefreshIdentityAsync();

        OnServer(patient: "Ana Pérez");
        await vm.RefreshIdentityAsync();

        Assert.Equal(("Ana Pérez", (string?)NnCode, true), (vm.Identity!.Primary, vm.Identity.Secondary, vm.Identity.IsIdentified));
    }

    [Fact]
    public async Task IdentifyingByDocumentUpdatesTheHeaderFromTheServerAnswer()
    {
        OnServer();
        var vm = new IdentifyPatientViewModel(_api, new CountingFingerprintService(), _navigator) { EmergencyId = _emergencyId };
        await vm.RefreshIdentityAsync();
        OnServer(patient: "Ana Pérez");
        vm.OpenDocumentFormCommand.Execute(null);
        vm.SelectedDocumentType = "Cédula";
        vm.DocumentNumber = "001-1234567-8";
        var reads = _api.Reads.Count;

        await vm.ResolveDocumentCommand.ExecuteAsync(null);
        await vm.ConfirmDocumentCommand.ExecuteAsync(null);

        Assert.True(vm.IsIdentified);
        Assert.Equal(("Ana Pérez", (string?)NnCode), (vm.Identity!.Primary, vm.Identity.Secondary));
        Assert.Equal(reads, _api.Reads.Count);
    }

    // The transfer screen already reads the emergency: the same read is reused, no second request.
    [Fact]
    public async Task TheTransferScreenReusesItsReadForTheHeader()
    {
        OnServer(patient: "Ana Pérez");
        var vm = new TransferViewModel(_api, new EmptyParamedicCatalog(), _navigator) { EmergencyId = _emergencyId };

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Single(_api.Reads);
        Assert.Equal(("Ana Pérez", (string?)NnCode), (vm.Identity!.Primary, vm.Identity.Secondary));
    }

    [Fact]
    public async Task AFailedReadKeepsTheHeaderAndNeverBlocksTheScreen()
    {
        OnServer();
        var vm = new VitalSignsViewModel(_api, _navigator) { EmergencyId = _emergencyId };
        await vm.RefreshIdentityAsync();
        _api.NextReadFailure = new MobileApiException(MobileApiError.Network);

        await vm.RefreshIdentityAsync();

        Assert.Equal(NnCode, vm.Identity!.Primary);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task AnInvalidCodeFromTheServerFallsBackSafely()
    {
        OnServer(code: "garbage");
        var vm = new ProceduresViewModel(_api, new EmptyParamedicCatalog(), _navigator) { EmergencyId = _emergencyId };

        await vm.RefreshIdentityAsync();

        Assert.Equal(EmergencyIdentity.UnknownNnCode, vm.Identity!.Primary);
    }

    // An older read never overwrites the header of a newer one.
    [Fact]
    public async Task ALateReadIsIgnored()
    {
        OnServer();
        var vm = new VitalSignsViewModel(_api, _navigator) { EmergencyId = _emergencyId };
        _api.HoldReads = true;
        var older = vm.RefreshIdentityAsync();
        var olderRead = _api.NextPendingRead();
        var newer = vm.RefreshIdentityAsync();
        _api.NextPendingRead().SetResult(OnServer(patient: "Ana Pérez"));
        await newer;

        olderRead.SetResult(new EmergencyDto { Id = _emergencyId, TemporaryPatientCode = NnCode });
        await older;

        Assert.Equal("Ana Pérez", vm.Identity!.Primary);
    }

    [Fact]
    public async Task InicioGoesBackToTheParamedicHomeReplacingTheStack()
    {
        var vm = new MedicationsViewModel(_api, new EmptyParamedicCatalog(), _navigator) { EmergencyId = _emergencyId };

        await vm.GoHomeCommand.ExecuteAsync(null);

        Assert.Equal(ParamedicRoutes.HomeRoot, Assert.Single(_navigator.Navigations).Route);
    }
}

// GET api/mobile/emergencies/active contra el contrato del Mobile BFF.
public class ActiveEmergenciesContractTests
{
    private sealed class JsonBff(string json) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method.Method} {request.RequestUri!.AbsolutePath}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private static HttpParamedicApiService Api(HttpMessageHandler bff) =>
        new(new MobileApiClient(new HttpClient(bff) { BaseAddress = new Uri("http://sciph.test/") }), new MockParamedicApiService());

    [Fact]
    public async Task TheListIsReadFromTheActiveEndpointWithItsTextStatuses()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var bff = new JsonBff(
            "[" +
            $"{{\"id\":\"{first}\",\"temporaryPatientCode\":\"NN-0123456789ABCDEF0123456789ABCDEF\",\"patientDisplayName\":null," +
            "\"status\":\"Transferred\",\"startedAtUtc\":\"2026-11-09T12:00:00+00:00\",\"chiefComplaint\":\"Politrauma\",\"activeTransferStatus\":\"InTransit\"}," +
            $"{{\"id\":\"{second}\",\"temporaryPatientCode\":\"NN-FEDCBA9876543210FEDCBA9876543210\",\"patientDisplayName\":\"Ana Pérez\"," +
            "\"status\":\"InProgress\",\"startedAtUtc\":\"2026-11-09T11:00:00+00:00\",\"chiefComplaint\":null,\"activeTransferStatus\":null}" +
            "]");

        var items = await Api(bff).GetActiveEmergenciesAsync();

        Assert.Equal(["GET /api/mobile/emergencies/active"], bff.Requests);
        Assert.Equal([first, second], items.Select(i => i.Id));
        Assert.Equal(("Transferred", (TransferStatus?)TransferStatus.InTransit, (string?)null, new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc)),
            (items[0].Status, items[0].ActiveTransferStatus, items[0].PatientDisplayName, items[0].StartedAt));
        Assert.Equal(("Ana Pérez", (TransferStatus?)null, (string?)null), (items[1].PatientDisplayName, items[1].ActiveTransferStatus, items[1].ChiefComplaint));
    }

    [Fact]
    public async Task NoActiveEmergenciesIsAnEmptyList() =>
        Assert.Empty(await Api(new JsonBff("[]")).GetActiveEmergenciesAsync());
}
