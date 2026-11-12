using System.Text.Json;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Tests.Paramedic;

// Identificación por huella REAL contra el contrato del Mobile BFF: elegir lector, abrir la sesión, consultar su estado
// (cada segundo, cancelable), mostrar el candidato y confirmar. La app nunca genera ni envía un id de paciente: solo la
// confirmación del servidor identifica la emergencia. Sin coincidencia, con error o vencida, documento y NN siguen ahí.
public class FingerprintIdentificationTests
{
    private readonly FakeFingerprintBff _bff = new();
    private readonly MockParamedicApiService _mock = new();
    private readonly FakeNavigator _navigator = new();
    private readonly PollGate _gate = new();
    private readonly Guid _emergencyId = Guid.NewGuid();

    public FingerprintIdentificationTests() => _mock.Adopt(new EmergencyDto { Id = _emergencyId });

    private IdentifyPatientViewModel ViewModel() =>
        new(new HttpParamedicApiService(new FakeDocumentIdentificationBff().Client(), _mock),
            new HttpFingerprintIdentificationService(_bff.Client()), _navigator, _gate.Delay)
        { EmergencyId = _emergencyId };

    private JsonElement Body(int index) => JsonDocument.Parse(_bff.Bodies[index]).RootElement;

    private string SessionsPath => $"/api/mobile/emergencies/{_emergencyId}/identify/fingerprint/sessions";

    // Lectores cargados y lectura abierta con el único lector.
    private async Task<IdentifyPatientViewModel> ReadingAsync()
    {
        var vm = ViewModel();
        await vm.IdentifyByFingerprintCommand.ExecuteAsync(null);
        await vm.StartFingerprintReadingCommand.ExecuteAsync(null);
        return vm;
    }

    // El lector responde lo guionado: la consulta corre sin esperas hasta un estado que la termina.
    private async Task RunPollingAsync(IdentifyPatientViewModel vm)
    {
        _gate.Free = true;
        _gate.Tick();
        await vm.FingerprintPolling!.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TheReadersAreLoadedAndASingleOneIsChosen()
    {
        var vm = ViewModel();
        Assert.False(vm.ShowFingerprintPanel);

        await vm.IdentifyByFingerprintCommand.ExecuteAsync(null);

        Assert.Equal("GET /api/mobile/devices/fingerprint-readers", Assert.Single(_bff.Paths));
        Assert.True(vm.ShowFingerprintPanel);
        Assert.Equal(FakeFingerprintBff.ReaderA, Assert.Single(vm.Readers).Id);
        Assert.Equal("Ambulancia 1 (ESP32-AMB-01)", vm.SelectedReader!.DisplayName);
        Assert.True(vm.StartFingerprintReadingCommand.CanExecute(null));

        _bff.Readers.Add(FakeFingerprintBff.ReaderB);
        var several = ViewModel();
        await several.IdentifyByFingerprintCommand.ExecuteAsync(null);
        Assert.Null(several.SelectedReader);
        Assert.False(several.StartFingerprintReadingCommand.CanExecute(null));

        _bff.Readers.Clear();
        var none = ViewModel();
        await none.IdentifyByFingerprintCommand.ExecuteAsync(null);
        Assert.Equal(ParamedicMessages.NoFingerprintReaders, none.FingerprintMessage);
        Assert.True(none.ContinueWithoutIdentifyingCommand.CanExecute(null));
    }

    // Lectura completa: la apertura lleva solo clave y lector; el candidato se muestra; la confirmación lleva solo su clave
    // y la emergencia queda identificada por el servidor.
    [Fact]
    public async Task AMatchShowsTheCandidateAndOnlyTheConfirmationIdentifies()
    {
        _bff.Script.Enqueue("Pending");
        _bff.Script.Enqueue("Matched");

        var vm = await ReadingAsync();

        Assert.Equal($"POST {SessionsPath}", _bff.Paths[1]);
        Assert.Equal(["clientRequestId", "deviceId"], Body(0).EnumerateObject().Select(p => p.Name));
        Assert.Equal(FakeFingerprintBff.ReaderA, Body(0).GetProperty("deviceId").GetGuid());
        Assert.True(vm.IsWaitingForReader);
        Assert.Equal(ParamedicMessages.FingerprintWaiting, vm.FingerprintMessage);
        Assert.False(vm.IsIdentified);
        // Mientras espera, documento y NN siguen disponibles.
        Assert.True(vm.ContinueWithoutIdentifyingCommand.CanExecute(null));

        await RunPollingAsync(vm);

        Assert.True(vm.HasCandidate);
        Assert.Equal(("Ana Pérez", "14/03/1985"), (vm.CandidateName, vm.CandidateBirthDate));
        Assert.False(vm.IsIdentified);
        Assert.False(_bff.Identified);

        await vm.ConfirmFingerprintCommand.ExecuteAsync(null);

        var confirm = Body(1);
        Assert.Equal(["clientRequestId"], confirm.EnumerateObject().Select(p => p.Name));
        Assert.True(_bff.Identified);
        Assert.True(vm.IsIdentified);
        Assert.Equal("Ana Pérez", vm.PatientDisplayName);
        Assert.False(vm.ShowFingerprintPanel);
        Assert.DoesNotContain(_bff.Bodies, b => b.Contains("patient", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("NoMatch", ParamedicMessages.FingerprintNoMatch)]
    [InlineData("Failed:LowQuality", ParamedicMessages.FingerprintLowQuality)]
    [InlineData("Failed:SensorError", ParamedicMessages.FingerprintSensorError)]
    [InlineData("Expired", ParamedicMessages.FingerprintExpired)]
    [InlineData("Matched:NoCandidate", ParamedicMessages.FingerprintNoMatch)]
    public async Task AReadingWithoutCandidateShowsWhyAndKeepsTheOtherWays(string answer, string message)
    {
        _bff.Script.Enqueue(answer);
        var vm = await ReadingAsync();

        await RunPollingAsync(vm);

        Assert.Equal(message, vm.FingerprintMessage);
        Assert.False(vm.HasCandidate);
        Assert.False(vm.IsWaitingForReader);
        Assert.False(vm.IsIdentified);
        Assert.True(vm.CanIdentify);
        Assert.True(vm.ContinueWithoutIdentifyingCommand.CanExecute(null));
        vm.OpenDocumentFormCommand.Execute(null);
        Assert.True(vm.ShowDocumentForm);
        Assert.False(vm.ConfirmFingerprintCommand.CanExecute(null));

        // Otra lectura es otra sesión, con otra clave.
        _gate.Free = false;
        await vm.StartFingerprintReadingCommand.ExecuteAsync(null);
        Assert.Equal(2, _bff.Opened);
        var openKeys = _bff.Bodies.Where(b => b.Contains("deviceId", StringComparison.Ordinal))
            .Select(b => JsonDocument.Parse(b).RootElement.GetProperty("clientRequestId").GetGuid()).ToList();
        Assert.Equal(2, openKeys.Distinct().Count());
        // Una coincidencia sin candidato se suelta en el servidor para poder leer de nuevo.
        Assert.Equal(answer == "Matched:NoCandidate", _bff.Paths.Any(p => p.EndsWith("/cancel", StringComparison.Ordinal)));
        vm.StopFingerprintPolling();
    }

    // Sin red al abrir, o respuesta perdida: el reintento lleva la misma clave y el servidor devuelve la misma sesión.
    [Fact]
    public async Task ARetriedOpeningKeepsItsKeyAndOpensOneSession()
    {
        var vm = ViewModel();
        await vm.IdentifyByFingerprintCommand.ExecuteAsync(null);
        _bff.UnreachableLeft = 1;

        await vm.StartFingerprintReadingCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.False(vm.IsWaitingForReader);

        _bff.LostResponsesLeft = 1;
        await vm.StartFingerprintReadingCommand.ExecuteAsync(null);
        await vm.StartFingerprintReadingCommand.ExecuteAsync(null);

        Assert.True(vm.IsWaitingForReader);
        Assert.Equal(1, _bff.Opened);
        Assert.Single(_bff.Bodies.Select(b => JsonDocument.Parse(b).RootElement.GetProperty("clientRequestId").GetGuid()).Distinct());
        vm.StopFingerprintPolling();
    }

    // La página deja de verse: la consulta se detiene y no vuelve a llamar; al volver, se reanuda.
    [Fact]
    public async Task ThePollingStopsWhenThePageDisappearsAndResumesWhenItAppears()
    {
        var vm = await ReadingAsync();
        _gate.Tick();
        await WaitUntilAsync(() => _bff.SessionReads == 1);

        vm.StopFingerprintPolling();
        await vm.FingerprintPolling!.WaitAsync(TimeSpan.FromSeconds(5));
        _gate.Tick(3);
        await Task.Delay(50);
        Assert.Equal(1, _bff.SessionReads);
        Assert.True(vm.IsWaitingForReader);

        _bff.Script.Enqueue("NoMatch");
        vm.ResumeFingerprintPolling();
        await RunPollingAsync(vm);
        Assert.Equal(ParamedicMessages.FingerprintNoMatch, vm.FingerprintMessage);
    }

    // Cancelar avisa al servidor, detiene la consulta y deja elegir de nuevo.
    [Fact]
    public async Task CancellingTheReadingStopsThePollingAndEndsTheSession()
    {
        var vm = await ReadingAsync();

        await vm.CancelFingerprintCommand.ExecuteAsync(null);
        await vm.FingerprintPolling!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal($"POST {SessionsPath}/{_bff.Paths.Last().Split('/')[^2]}/cancel", _bff.Paths.Last());
        Assert.Equal("Cancelled", _bff.StatusOf(Guid.Parse(_bff.Paths.Last().Split('/')[^2])));
        Assert.Equal(ParamedicMessages.FingerprintCancelled, vm.FingerprintMessage);
        Assert.False(vm.IsWaitingForReader);
        Assert.True(vm.CanChooseReader);
        _gate.Tick(3);
        await Task.Delay(50);
        Assert.Equal(0, _bff.SessionReads);
    }

    // Sin conexión al consultar: se sigue intentando mientras la lectura no venza, y el resultado llega después.
    [Fact]
    public async Task ALostConnectionWhilePollingKeepsTryingUntilTheAnswer()
    {
        _bff.UnreachablePollsLeft = 2;
        _bff.Script.Enqueue("Matched");
        var vm = await ReadingAsync();

        await RunPollingAsync(vm);

        Assert.True(vm.HasCandidate);
        Assert.Equal(3, _bff.SessionReads);
    }

    // La confirmación falla (la lectura venció o cambió): mensaje seguro y el estado real de la sesión.
    [Fact]
    public async Task ARejectedConfirmationShowsASafeMessageAndTheRealState()
    {
        _bff.Script.Enqueue("Matched");
        var vm = await ReadingAsync();
        await RunPollingAsync(vm);
        _bff.NextStatus = System.Net.HttpStatusCode.Conflict;

        await vm.ConfirmFingerprintCommand.ExecuteAsync(null);

        Assert.Equal(ParamedicMessages.FingerprintConfirmConflict, vm.ErrorMessage);
        Assert.False(vm.IsIdentified);
        Assert.True(vm.HasCandidate);
        Assert.DoesNotContain("Forced", vm.ErrorMessage);

        // El reintento usa la misma clave de confirmación y ahora identifica.
        await vm.ConfirmFingerprintCommand.ExecuteAsync(null);
        Assert.True(vm.IsIdentified);
        var keys = _bff.Bodies.Skip(1).Select(b => JsonDocument.Parse(b).RootElement.GetProperty("clientRequestId").GetGuid()).Distinct();
        Assert.Single(keys);
    }

    // La coincidencia ya no se puede confirmar (el lector dejó de ser válido tras leer): 409 con el mensaje seguro, la sesión
    // vuelve sin candidato y se suelta en el servidor. Se puede leer de nuevo con otra clave, ir por documento o seguir NN.
    [Fact]
    public async Task AMatchThatCanNoLongerBeConfirmedIsReleasedOnTheServer()
    {
        _bff.Script.Enqueue("Matched");
        var vm = await ReadingAsync();
        await RunPollingAsync(vm);
        Assert.True(vm.HasCandidate);
        var sessionId = Guid.Parse(_bff.Paths.Last().Split('/')[^1]);
        _bff.WithdrawCandidates();

        await vm.ConfirmFingerprintCommand.ExecuteAsync(null);

        Assert.Equal(ParamedicMessages.FingerprintConfirmConflict, vm.ErrorMessage);
        Assert.Equal(ParamedicMessages.FingerprintNoMatch, vm.FingerprintMessage);
        Assert.Equal($"POST {SessionsPath}/{sessionId}/cancel", _bff.Paths.Last());
        Assert.Equal("Cancelled", _bff.StatusOf(sessionId));
        Assert.False(_bff.Identified);
        Assert.False(vm.IsIdentified);
        Assert.False(vm.HasCandidate);
        Assert.False(vm.IsWaitingForReader);
        Assert.True(vm.CanChooseReader);
        Assert.False(vm.ConfirmFingerprintCommand.CanExecute(null));
        Assert.DoesNotContain("revoked", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        Assert.True(vm.ContinueWithoutIdentifyingCommand.CanExecute(null));
        vm.OpenDocumentFormCommand.Execute(null);
        Assert.True(vm.ShowDocumentForm);
        _gate.Free = false;
        await vm.StartFingerprintReadingCommand.ExecuteAsync(null);
        Assert.Equal(2, _bff.Opened);
        Assert.True(vm.IsWaitingForReader);
        var openKeys = _bff.Bodies.Where(b => b.Contains("deviceId", StringComparison.Ordinal))
            .Select(b => JsonDocument.Parse(b).RootElement.GetProperty("clientRequestId").GetGuid()).Distinct();
        Assert.Equal(2, openKeys.Count());
        vm.StopFingerprintPolling();
    }

    // Identificar por documento o continuar como NN suelta el lector: la lectura pendiente se cancela.
    [Fact]
    public async Task ContinuingAsNnWhileReadingCancelsTheSession()
    {
        var vm = await ReadingAsync();

        await vm.ContinueWithoutIdentifyingCommand.ExecuteAsync(null);

        Assert.Contains(_bff.Paths, p => p.EndsWith("/cancel", StringComparison.Ordinal));
        Assert.Equal(ParamedicRoutes.VitalSigns, Assert.Single(_navigator.Navigations).Route);
        Assert.False(vm.IsWaitingForReader);
        Assert.False(vm.IsIdentified);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    // Retardo de la consulta controlado por el test: espera un "tic", o ninguno cuando está libre.
    private sealed class PollGate
    {
        private readonly SemaphoreSlim _ticks = new(0);

        public bool Free { get; set; }

        public Task Delay(TimeSpan interval, CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken)
            : Free ? Task.CompletedTask
            : _ticks.WaitAsync(cancellationToken);

        public void Tick(int count = 1) => _ticks.Release(count);
    }
}
