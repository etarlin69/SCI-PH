using System.Net;
using System.Text.Json;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Tests.Paramedic;

// Signos vitales REALES (POST api/mobile/emergencies/{id}/vital-signs) sobre la emergencia real: una toma por envío,
// con su propia clave y su hora, conservadas en los reintentos. Procedimientos y medicamentos siguen en mock.
public class VitalSignsTests
{
    private readonly FakeVitalSignsBff _bff = new();
    private readonly MockParamedicApiService _mock = new();
    private readonly FakeNavigator _navigator = new();
    private readonly Guid _emergencyId = Guid.NewGuid();

    public VitalSignsTests() => _mock.Adopt(new EmergencyDto { Id = _emergencyId });

    private HttpParamedicApiService Service() => new(_bff.Client(), _mock);

    private VitalSignsViewModel ViewModel() => new(Service(), _navigator) { EmergencyId = _emergencyId };

    private JsonElement Body(int index) => JsonDocument.Parse(_bff.Bodies[index]).RootElement;

    private static void FillAllSeven(VitalSignsViewModel vm)
    {
        vm.HeartRate = "88";
        vm.RespiratoryRate = "18";
        vm.SystolicPressure = "120";
        vm.DiastolicPressure = "80";
        vm.OxygenSaturation = "97";
        vm.Temperature = "37,5";
        vm.Glasgow = "15";
    }

    [Fact]
    public async Task AllSevenFieldsAreSentOneToOneInASingleRequest()
    {
        var vm = ViewModel();
        FillAllSeven(vm);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal($"POST /api/mobile/emergencies/{_emergencyId}/vital-signs", Assert.Single(_bff.Paths));
        var body = Body(0);
        Assert.Equal(
            ["clientRequestId", "recordedAt", "heartRate", "respiratoryRate", "systolicPressure", "diastolicPressure", "oxygenSaturation", "temperature", "glasgow"],
            body.EnumerateObject().Select(p => p.Name));
        Assert.Equal((88, 18, 120, 80, 97, 15), (body.GetProperty("heartRate").GetInt32(), body.GetProperty("respiratoryRate").GetInt32(),
            body.GetProperty("systolicPressure").GetInt32(), body.GetProperty("diastolicPressure").GetInt32(),
            body.GetProperty("oxygenSaturation").GetInt32(), body.GetProperty("glasgow").GetInt32()));
        Assert.Equal(37.5m, body.GetProperty("temperature").GetDecimal());
        Assert.EndsWith("+00:00", body.GetProperty("recordedAt").GetString());
        Assert.DoesNotContain("unit", _bff.Bodies[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnlyTheCapturedValuesAreSent()
    {
        var vm = ViewModel();
        vm.HeartRate = "110";
        vm.OxygenSaturation = "91";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["clientRequestId", "recordedAt", "heartRate", "oxygenSaturation"], Body(0).EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void AtLeastOneValueIsRequiredAndTheLimitsAreTheServerOnes()
    {
        var vm = ViewModel();
        Assert.False(vm.SaveCommand.CanExecute(null));

        vm.Glasgow = "2";
        Assert.True(vm.HasGlasgowError);
        Assert.False(vm.SaveCommand.CanExecute(null));

        vm.Glasgow = "3";
        vm.Temperature = "37.555";
        Assert.True(vm.HasTemperatureError);
        vm.Temperature = "37.55";
        Assert.False(vm.HasTemperatureError);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task ARecordedReadingGoesToProceduresWithTheSameEmergency()
    {
        var vm = ViewModel();
        vm.HeartRate = "90";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(HttpStatusCode.Created, Assert.Single(_bff.Answers));
        Assert.Null(vm.ErrorMessage);
        var (route, parameters) = Assert.Single(_navigator.Navigations);
        Assert.Equal(ParamedicRoutes.Procedures, route);
        Assert.Equal(_emergencyId, parameters!["EmergencyId"]);
        // The next reading is a new one: new key and new time.
        vm.HeartRate = "95";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotEqual(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
    }

    [Fact]
    public async Task ANetworkFailureKeepsValuesKeyAndTimeForTheRetry()
    {
        var vm = ViewModel();
        FillAllSeven(vm);
        _bff.UnreachableLeft = 1;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);
        Assert.Equal(("88", "37,5", "15"), (vm.HeartRate, vm.Temperature, vm.Glasgow));
        Assert.Empty(_navigator.Navigations);

        await Task.Delay(20);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Single(_bff.Bodies);
        Assert.Equal(1, _bff.Recorded);
        Assert.Single(_navigator.Navigations);
    }

    // The server recorded it but the answer was lost: the retry with the same key and time is a replay (200).
    [Fact]
    public async Task ALostResponseIsReplayedWithTheSameKeyAndTimeAndGoesToProcedures()
    {
        var vm = ViewModel();
        vm.HeartRate = "90";
        _bff.LostResponsesLeft = 1;

        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);
        await Task.Delay(20);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.OK], _bff.Answers);
        Assert.Equal(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
        Assert.Equal(Body(0).GetProperty("recordedAt").GetString(), Body(1).GetProperty("recordedAt").GetString());
        Assert.Equal(1, _bff.Recorded);
        Assert.Equal(ParamedicRoutes.Procedures, Assert.Single(_navigator.Navigations).Route);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ARejectedReadingStaysWithASafeMessageAndKeepsItsKey(HttpStatusCode status)
    {
        var vm = ViewModel();
        vm.HeartRate = "90";
        _bff.NextStatus = status;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.DoesNotContain("Forced", vm.ErrorMessage);
        Assert.Equal("90", vm.HeartRate);
        Assert.Empty(_navigator.Navigations);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
    }

    // Same key with other values (changed after a lost answer): 409, a safe message, no new key in silence.
    [Fact]
    public async Task AConflictShowsASafeMessageAndKeepsTheKey()
    {
        var vm = ViewModel();
        vm.HeartRate = "90";
        _bff.LostResponsesLeft = 1;
        await vm.SaveCommand.ExecuteAsync(null);

        vm.HeartRate = "91";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ParamedicMessages.VitalSignsConflict, vm.ErrorMessage);
        Assert.Empty(_navigator.Navigations);
        Assert.Equal(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
    }

    [Fact]
    public async Task ProceduresAndMedicationsStayMock()
    {
        var service = Service();

        await service.RegisterProcedureAsync(_emergencyId, new RegisterProcedureDto { ProcedureCode = "PROC-001" });
        await service.RegisterMedicationAsync(_emergencyId, new RegisterMedicationDto { MedicationCode = "MED-001" });

        Assert.Empty(_bff.Paths);
    }
}
