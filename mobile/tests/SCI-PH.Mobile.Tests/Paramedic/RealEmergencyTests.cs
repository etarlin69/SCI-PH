using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Services;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Tests.Paramedic;

// Abrir y leer la emergencia contra el contrato real del Mobile BFF (POST/GET api/mobile/emergencies). Ningún paso del área
// paramédica queda en mock.
public class RealEmergencyTests
{
    private readonly FakeEmergenciesBff _bff = new();
    private readonly MockParamedicApiService _mock = new();
    private readonly FakeNavigator _navigator = new();

    private HttpParamedicApiService Service() => new(_bff.Client(), _mock);

    private NewEmergencyViewModel ViewModel() => new(Service(), _navigator) { ChiefComplaint = "  Dolor torácico " };

    private Guid NavigatedEmergencyId(int index = 0) => (Guid)_navigator.Navigations[index].Parameters!["EmergencyId"];

    [Fact]
    public void TheParamedicFeatureUsesTheRealServiceForTheEmergency()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMobileApiClient>(_bff.Client());

        services.AddParamedicFeature();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<HttpParamedicApiService>(provider.GetRequiredService<IParamedicApiService>());
        // Catálogos e identificación por huella reales.
        Assert.IsType<HttpParamedicCatalogService>(provider.GetRequiredService<IParamedicCatalogService>());
        Assert.IsType<HttpFingerprintIdentificationService>(provider.GetRequiredService<IFingerprintIdentificationService>());
    }

    [Fact]
    public async Task CreatePostsExactlyTheV1ContractWithoutLocation()
    {
        var key = Guid.NewGuid();
        var startedAt = new DateTime(2026, 10, 23, 14, 0, 0, DateTimeKind.Utc);

        var emergency = await Service().CreateEmergencyAsync(new CreateEmergencyDto { ClientRequestId = key, StartedAt = startedAt, ChiefComplaint = "Dolor torácico" });

        Assert.Equal("POST /api/mobile/emergencies", Assert.Single(_bff.Paths));
        var body = JsonDocument.Parse(Assert.Single(_bff.Bodies)).RootElement;
        Assert.Equal(["clientRequestId", "startedAt", "chiefComplaint"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(key, body.GetProperty("clientRequestId").GetGuid());
        Assert.Equal(new DateTimeOffset(startedAt), body.GetProperty("startedAt").GetDateTimeOffset());
        Assert.EndsWith("+00:00", body.GetProperty("startedAt").GetString());
        Assert.DoesNotContain("location", _bff.Bodies[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("latitude", _bff.Bodies[0], StringComparison.OrdinalIgnoreCase);

        Assert.Equal((EmergencyStatus.Open, FakeEmergenciesBff.TemporaryPatientCode), (emergency.Status, emergency.TemporaryPatientCode));
        Assert.Null(emergency.Patient);
    }

    [Fact]
    public async Task GetReadsTheRealEmergencyAndAMissingOneIsNull()
    {
        var created = await Service().CreateEmergencyAsync(new CreateEmergencyDto { ChiefComplaint = "Dolor" });

        var read = await Service().GetEmergencyAsync(created.Id);

        Assert.Equal($"GET /api/mobile/emergencies/{created.Id}", _bff.Paths[1]);
        Assert.Equal(created.Id, read!.Id);
        Assert.Null(await Service().GetEmergencyAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ACreatedEmergencyNavigatesWithTheRealId()
    {
        var vm = ViewModel();

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(vm.HasError);
        Assert.Equal(ParamedicRoutes.IdentifyPatient, Assert.Single(_navigator.Navigations).Route);
        Assert.Equal(1, _bff.Created);
        Assert.Equal("Dolor torácico", JsonDocument.Parse(_bff.Bodies[0]).RootElement.GetProperty("chiefComplaint").GetString());
        Assert.Equal(string.Empty, vm.ChiefComplaint);
        Assert.NotEqual(Guid.Empty, NavigatedEmergencyId());
    }

    // El servidor la creó pero la respuesta se perdió: el reintento lleva la misma clave y los mismos datos, el servidor
    // responde 200 con la misma emergencia y la app sigue con ese id.
    [Fact]
    public async Task ANetworkFailureStaysOnScreenAndTheRetryReplaysTheSameEmergency()
    {
        _bff.LostResponsesLeft = 1;
        var vm = ViewModel();

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);
        Assert.Empty(_navigator.Navigations);
        Assert.Equal("  Dolor torácico ", vm.ChiefComplaint);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(vm.HasError);
        Assert.Equal(1, _bff.Created);
        var (first, retry) = (JsonDocument.Parse(_bff.Bodies[0]).RootElement, JsonDocument.Parse(_bff.Bodies[1]).RootElement);
        Assert.Equal(first.GetProperty("clientRequestId").GetGuid(), retry.GetProperty("clientRequestId").GetGuid());
        Assert.Equal(first.GetProperty("startedAt").GetString(), retry.GetProperty("startedAt").GetString());
        Assert.Equal(first.GetProperty("chiefComplaint").GetString(), retry.GetProperty("chiefComplaint").GetString());
        Assert.Equal(ParamedicRoutes.IdentifyPatient, Assert.Single(_navigator.Navigations).Route);
    }

    [Fact]
    public async Task AnUnreachableServerKeepsTheRequestForTheRetry()
    {
        _bff.UnreachableLeft = 2;
        var vm = ViewModel();

        await vm.SaveCommand.ExecuteAsync(null);
        await vm.SaveCommand.ExecuteAsync(null);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, _bff.Created);
        Assert.Single(_navigator.Navigations);
    }

    [Fact]
    public async Task AReplayFromTheServerNavigatesWithTheSameId()
    {
        var key = Guid.NewGuid();
        var startedAt = DateTime.UtcNow;
        var service = Service();
        var created = await service.CreateEmergencyAsync(new CreateEmergencyDto { ClientRequestId = key, StartedAt = startedAt, ChiefComplaint = "Dolor" });

        var replayed = await service.CreateEmergencyAsync(new CreateEmergencyDto { ClientRequestId = key, StartedAt = startedAt, ChiefComplaint = "Dolor" });

        Assert.Equal(created.Id, replayed.Id);
        Assert.Equal(1, _bff.Created);
    }

    // El texto cambió tras un fallo de resultado desconocido: el servidor rechaza la clave reutilizada. La app lo indica y
    // no genera otra clave en silencio (podría abrir una segunda emergencia).
    [Fact]
    public async Task AReusedKeyWithOtherDataShowsTheSafeConflictMessage()
    {
        _bff.LostResponsesLeft = 1;
        var vm = ViewModel();
        await vm.SaveCommand.ExecuteAsync(null);

        vm.ChiefComplaint = "Caída de altura";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ParamedicMessages.EmergencyRequestReused, vm.ErrorMessage);
        Assert.Empty(_navigator.Navigations);

        await vm.SaveCommand.ExecuteAsync(null);

        var keys = _bff.Bodies.Select(b => JsonDocument.Parse(b).RootElement.GetProperty("clientRequestId").GetGuid()).Distinct();
        Assert.Single(keys);
        Assert.Equal(1, _bff.Created);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, MobileApiErrorMessages.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden, MobileApiErrorMessages.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError, ParamedicMessages.CreateEmergencyFailed)]
    public async Task ServerErrorsShowSafeMessages(HttpStatusCode status, string expected)
    {
        _bff.NextStatus = status;
        var vm = ViewModel();

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(expected, vm.ErrorMessage);
        Assert.DoesNotContain("test", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_navigator.Navigations);
    }
}
