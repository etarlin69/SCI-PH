using System.Net;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Scanner;
using SCI_PH.Mobile.Features.Paramedic.Services;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;
using SCI_PH.Mobile.Tests.Auth;
using ZXing.Net.Maui;

namespace SCI_PH.Mobile.Tests.Paramedic;

/// <summary>Permiso de cámara guionado: cada consulta y cada petición toman el siguiente estado (o el último).</summary>
internal sealed class FakeCameraPermission : ICameraPermissionService
{
    public bool IsScannerSupported { get; set; } = true;

    public CameraPermissionStatus CheckResult { get; set; } = CameraPermissionStatus.Granted;

    public Queue<CameraPermissionStatus> RequestResults { get; } = new();

    public int Checks { get; private set; }

    public int Requests { get; private set; }

    public int SettingsOpened { get; private set; }

    /// <summary>La plataforma falla al consultar o pedir el permiso.</summary>
    public bool Throws { get; set; }

    public Task<CameraPermissionStatus> CheckAsync()
    {
        Checks++;
        return Throws ? Task.FromException<CameraPermissionStatus>(new InvalidOperationException("camera")) : Task.FromResult(CheckResult);
    }

    public Task<CameraPermissionStatus> RequestAsync()
    {
        Requests++;
        if (Throws)
        {
            return Task.FromException<CameraPermissionStatus>(new InvalidOperationException("camera"));
        }

        var result = RequestResults.TryDequeue(out var next) ? next : CheckResult;
        CheckResult = result;
        return Task.FromResult(result);
    }

    public void OpenSettings() => SettingsOpened++;
}

/// <summary>Esperas del escáner (la pausa tras un código no válido) que el test libera.</summary>
internal sealed class ManualDelay
{
    private readonly List<TaskCompletionSource> _pending = [];

    public int Pending => _pending.Count(t => !t.Task.IsCompleted);

    public Task Delay(TimeSpan _, CancellationToken cancellationToken)
    {
        // Continuaciones en línea: al liberar, el escáner ya reaccionó.
        var wait = new TaskCompletionSource();
        cancellationToken.Register(() => wait.TrySetCanceled(cancellationToken));
        _pending.Add(wait);
        return wait.Task;
    }

    public void ReleaseAll()
    {
        foreach (var wait in _pending.ToList())
        {
            wait.TrySetResult();
        }
    }
}

internal static class StaffSession
{
    public static ClinicalSessionService SignedIn(Guid? userId = null)
    {
        var session = new ClinicalSessionService();
        SignIn(session, userId ?? Guid.NewGuid());
        return session;
    }

    public static void SignIn(IClinicalSessionService session, Guid userId)
    {
        session.SetAuthentication($"access-{userId:N}", DateTimeOffset.UtcNow.AddMinutes(15));
        session.SetCurrentUser(new ClinicalUser(userId, "paramedico", null, ["Paramedic"], [ClinicalPermissions.EmergenciesOperate]));
    }
}

// El escáner del paramédico contra el contrato real del QR (ADR-015): leer solo pide el candidato; solo "Confirmar paciente"
// identifica, una vez, con una clave que se conserva en los reintentos. Sin cámara ni MAUI: la página entrega lo leído.
public class QrScannerTests
{
    private readonly FakeQrIdentificationBff _bff = new();
    private readonly FakeCameraPermission _camera = new();
    private readonly FakeNavigator _navigator = new();
    private readonly ManualDelay _delay = new();
    private readonly ClinicalSessionService _session = StaffSession.SignedIn();
    private readonly MockParamedicApiService _mock = new();
    private readonly Guid _emergencyId = Guid.NewGuid();

    private string ResolvePath => $"POST /api/mobile/emergencies/{_emergencyId}/identify/qr/resolve";

    private string ConfirmPath => $"POST /api/mobile/emergencies/{_emergencyId}/identify/qr/confirm";

    private QrScannerViewModel Scanner(MobileApiClient? client = null) =>
        new(new HttpParamedicApiService(client ?? _bff.Client(), _mock), _camera, _session, _navigator, _delay.Delay)
        { EmergencyId = _emergencyId };

    private async Task<QrScannerViewModel> ScanningAsync(MobileApiClient? client = null)
    {
        var scanner = Scanner(client);
        await scanner.ActivateAsync();
        Assert.Equal(QrScannerState.Scanning, scanner.State);
        Assert.True(scanner.IsDetecting);
        return scanner;
    }

    private async Task<QrScannerViewModel> CandidateAsync()
    {
        var scanner = await ScanningAsync();
        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);
        Assert.Equal(QrScannerState.Candidate, scanner.State);
        return scanner;
    }

    private static BarcodeResult Result(BarcodeFormat format, string value) => new() { Format = format, Value = value };

    // ---- Lectura ----

    [Fact]
    public async Task AValidScanResolvesOnceWithThePayloadOnlyInTheBody()
    {
        var scanner = await ScanningAsync();

        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);

        Assert.Equal(ResolvePath, Assert.Single(_bff.Paths));
        Assert.Equal("""{"payload":"sciphqr:v1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""", Assert.Single(_bff.Bodies));
        Assert.DoesNotContain(_bff.Paths, p => p.Contains("sciphqr", StringComparison.Ordinal));
        Assert.False(scanner.IsDetecting);
    }

    [Fact]
    public async Task TheCandidateShowsOnlyTheIdentityAndDoesNotIdentify()
    {
        var scanner = await CandidateAsync();

        Assert.Equal("Ana Pérez", scanner.CandidateName);
        Assert.Equal("14/03/1985", scanner.CandidateBirthDate);
        Assert.Equal("Cédula ***5678", scanner.CandidateDocument);
        Assert.True(scanner.HasCandidate);
        Assert.False(scanner.IsCameraActive);
        Assert.True(scanner.ConfirmCommand.CanExecute(null));
        Assert.Equal(0, _bff.Confirms);
        Assert.False(_bff.Identified);
        Assert.Empty(_navigator.Navigations);
    }

    [Fact]
    public async Task RepeatedEventsOfTheSameFrameResolveOnce()
    {
        var gate = new QrScanGate();
        var frame = new[] { Result(BarcodeFormat.QrCode, FakeQrIdentificationBff.PayloadA) };
        var accepted = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 50, _ =>
        {
            if (gate.TryAccept(frame) is { } value)
            {
                accepted.Add(value);
            }
        });

        Assert.Single(accepted);
        Assert.Null(gate.TryAccept(frame));

        // Aunque llegaran varias a la interfaz, el ViewModel procesa una sola.
        var resolveGate = _bff.ResolveGate = new TaskCompletionSource();
        var scanner = await ScanningAsync();
        var first = scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);
        var second = scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);
        var third = scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);
        resolveGate.SetResult();
        await Task.WhenAll(first, second, third);

        Assert.Equal(1, _bff.Resolves);
        Assert.Equal(QrScannerState.Candidate, scanner.State);

        gate.Rearm();
        Assert.Equal(FakeQrIdentificationBff.PayloadA, gate.TryAccept(frame));
    }

    [Fact]
    public void OnlyQrResultsAreAcceptedAndTheValueIsUntouched()
    {
        var gate = new QrScanGate();

        Assert.Null(gate.TryAccept([Result(BarcodeFormat.Ean13, "7501031311309"), Result(BarcodeFormat.Code128, "sciphqr:v1:x")]));
        Assert.Null(gate.TryAccept([Result(BarcodeFormat.Pdf417, FakeQrIdentificationBff.PayloadA)]));
        Assert.Null(gate.TryAccept([]));
        Assert.Null(gate.TryAccept(null));

        var padded = " " + FakeQrIdentificationBff.PayloadA + "\n";
        Assert.Equal(padded, gate.TryAccept([Result(BarcodeFormat.Ean13, "123"), Result(BarcodeFormat.QrCode, padded)]));
    }

    [Theory]
    [InlineData("https://example.com/qr")]
    [InlineData("SCIPHQR:V1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(" sciphqr:v1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("sciphqr:v2:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task ACodeThatIsNotFromSciPhNeverReachesTheServer(string value)
    {
        var scanner = await ScanningAsync();

        await scanner.ProcessScanAsync(value);

        Assert.Empty(_bff.Paths);
        Assert.Equal(ParamedicMessages.QrInvalidCode, scanner.StatusMessage);
        Assert.Equal(QrScannerState.Scanning, scanner.State);
        Assert.False(scanner.IsDetecting);

        // El mismo código sigue delante: la detección vuelve tras una pausa, nunca en el acto.
        _delay.ReleaseAll();
        await Task.Yield();
        Assert.True(scanner.IsDetecting);
        Assert.Empty(_bff.Paths);
    }

    [Fact]
    public async Task TheValueIsSentExactlyAsRead()
    {
        // Un QR con el prefijo correcto y algo de más va tal cual: el servidor decide (404).
        var scanner = await ScanningAsync();
        var trailing = FakeQrIdentificationBff.PayloadA + " ";

        await scanner.ProcessScanAsync(trailing);

        Assert.Contains("\"sciphqr:v1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA \"", Assert.Single(_bff.Bodies));
        Assert.Equal(ParamedicMessages.QrInvalidOrExpired, scanner.StatusMessage);
    }

    // ---- Confirmación ----

    [Fact]
    public async Task OnlyTheConfirmationIdentifiesOnceAndReturns()
    {
        var scanner = await CandidateAsync();

        await scanner.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(1, _bff.Confirms);
        Assert.Equal(1, _bff.IdentificationsCommitted);
        var body = _bff.Body(1);
        Assert.Equal(FakeQrIdentificationBff.PayloadA, body.GetProperty("payload").GetString());
        Assert.NotEqual(Guid.Empty, body.GetProperty("clientRequestId").GetGuid());
        Assert.Equal(2, body.EnumerateObject().Count());
        Assert.Equal(QrScannerState.Completed, scanner.State);
        Assert.Null(scanner.CandidateName);
        Assert.Equal("..", Assert.Single(_navigator.Navigations).Route);
    }

    [Fact]
    public async Task GoingBackIsBlockedOnlyWhileConfirmingAndNeverCancelsTheRequest()
    {
        var scanner = await CandidateAsync();
        Assert.False(scanner.BlocksNavigation(ShellNavigationSource.Pop));
        var confirmGate = _bff.ConfirmGate = new TaskCompletionSource();

        var confirming = scanner.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(QrScannerState.Confirming, scanner.State);
        Assert.True(scanner.BlocksNavigation(ShellNavigationSource.Pop));
        Assert.True(scanner.BlocksNavigation(ShellNavigationSource.PopToRoot));
        Assert.True(scanner.BlocksNavigation(ShellNavigationSource.Remove));
        Assert.Empty(_navigator.Navigations);
        Assert.False(confirming.IsCompleted);

        confirmGate.SetResult();
        await confirming;

        Assert.Equal(1, _bff.Confirms);
        Assert.Equal(1, _bff.IdentificationsCommitted);
        Assert.Equal(QrScannerState.Completed, scanner.State);
        Assert.False(scanner.BlocksNavigation(ShellNavigationSource.Pop));
        Assert.Equal("..", Assert.Single(_navigator.Navigations).Route);
    }

    // La salida al vencer la sesión va a otra raíz (//Entry): nunca se bloquea.
    [Theory]
    [InlineData(ShellNavigationSource.ShellItemChanged)]
    [InlineData(ShellNavigationSource.ShellSectionChanged)]
    [InlineData(ShellNavigationSource.ShellContentChanged)]
    [InlineData(ShellNavigationSource.Unknown)]
    public async Task TheMandatoryExitIsNeverBlockedWhileConfirming(ShellNavigationSource source)
    {
        var scanner = await CandidateAsync();
        var confirmGate = _bff.ConfirmGate = new TaskCompletionSource();
        var confirming = scanner.ConfirmCommand.ExecuteAsync(null);

        Assert.False(scanner.BlocksNavigation(source));

        confirmGate.SetResult();
        await confirming;
    }

    [Fact]
    public async Task ASessionChangeWhileConfirmingUnblocksAndDropsTheLateResult()
    {
        var scanner = await CandidateAsync();
        var confirmGate = _bff.ConfirmGate = new TaskCompletionSource();
        var confirming = scanner.ConfirmCommand.ExecuteAsync(null);

        StaffSession.SignIn(_session, Guid.NewGuid());

        Assert.False(scanner.BlocksNavigation(ShellNavigationSource.Pop));
        Assert.Null(scanner.CandidateName);
        confirmGate.SetResult();
        await confirming;
        Assert.Empty(_navigator.Navigations);
        Assert.NotEqual(QrScannerState.Completed, scanner.State);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task AfterAFailedConfirmationGoingBackIsAllowedAgainAndTheKeyIsKept(HttpStatusCode status)
    {
        var scanner = await CandidateAsync();
        _bff.NextStatuses.Enqueue(status);

        await scanner.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(QrScannerState.Candidate, scanner.State);
        Assert.False(scanner.BlocksNavigation(ShellNavigationSource.Pop));
        await scanner.ConfirmCommand.ExecuteAsync(null);

        var keys = _bff.Bodies.Where(b => b.Contains("clientRequestId", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
        Assert.Single(keys);
        Assert.Equal(QrScannerState.Completed, scanner.State);
    }

    [Fact]
    public async Task ADoubleTapConfirmsOnce()
    {
        var scanner = await CandidateAsync();
        var confirmGate = _bff.ConfirmGate = new TaskCompletionSource();

        var first = scanner.ConfirmCommand.ExecuteAsync(null);
        Assert.False(scanner.ConfirmCommand.CanExecute(null));
        var second = scanner.ConfirmCommand.ExecuteAsync(null);
        confirmGate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, _bff.Confirms);
        Assert.Equal(1, _bff.IdentificationsCommitted);
    }

    [Fact]
    public async Task CancellingNeverConfirms()
    {
        var scanner = await CandidateAsync();

        await scanner.CancelCommand.ExecuteAsync(null);

        Assert.Equal(0, _bff.Confirms);
        Assert.False(_bff.Identified);
        Assert.Null(scanner.CandidateName);
        Assert.Equal("..", Assert.Single(_navigator.Navigations).Route);
    }

    [Fact]
    public async Task ATimedOutConfirmationIsRetriedWithTheSameRequestId()
    {
        var scanner = await CandidateAsync();
        _bff.TimeoutsAfterProcessingLeft = 1;

        await scanner.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(QrScannerState.Candidate, scanner.State);
        Assert.Equal(ParamedicMessages.QrConfirmFailed, scanner.ErrorMessage);
        Assert.Equal("Ana Pérez", scanner.CandidateName);

        // El QR vence mientras tanto: el reintento con la misma clave igual devuelve la emergencia ya identificada.
        _bff.Valid.Clear();
        await scanner.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(2, _bff.Confirms);
        Assert.Equal(_bff.Body(1).GetProperty("clientRequestId").GetGuid(), _bff.Body(2).GetProperty("clientRequestId").GetGuid());
        Assert.Equal(1, _bff.IdentificationsCommitted);
        Assert.Equal(QrScannerState.Completed, scanner.State);
    }

    [Fact]
    public async Task ALostResponseIsRetriedWithTheSameRequestId()
    {
        var scanner = await CandidateAsync();
        _bff.LostResponsesLeft = 1;

        await scanner.ConfirmCommand.ExecuteAsync(null);
        await scanner.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(_bff.Body(1).GetProperty("clientRequestId").GetGuid(), _bff.Body(2).GetProperty("clientRequestId").GetGuid());
        Assert.Equal(1, _bff.IdentificationsCommitted);
        Assert.Equal(QrScannerState.Completed, scanner.State);
    }

    [Fact]
    public async Task A503OnConfirmKeepsTheCandidateAndTheRequestId()
    {
        var scanner = await CandidateAsync();
        _bff.NextStatuses.Enqueue(HttpStatusCode.ServiceUnavailable);

        await scanner.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(QrScannerState.Candidate, scanner.State);
        Assert.Equal(ParamedicMessages.QrConfirmFailed, scanner.ErrorMessage);
        Assert.False(_bff.Identified);

        await scanner.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(_bff.Body(1).GetProperty("clientRequestId").GetGuid(), _bff.Body(2).GetProperty("clientRequestId").GetGuid());
        Assert.Equal(QrScannerState.Completed, scanner.State);
    }

    [Fact]
    public async Task AQrThatExpiresBeforeTheConfirmationClearsTheCandidate()
    {
        var scanner = await CandidateAsync();
        _bff.Valid.Clear();

        await scanner.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(QrScannerState.Scanning, scanner.State);
        Assert.Equal(ParamedicMessages.QrInvalidOrExpired, scanner.StatusMessage);
        Assert.Null(scanner.CandidateName);
        Assert.Null(scanner.CandidateDocument);
        Assert.False(scanner.ConfirmCommand.CanExecute(null));
        Assert.False(_bff.Identified);

        // El paciente actualiza su QR: lectura nueva, candidato nuevo y otra clave.
        _bff.Valid.Add(FakeQrIdentificationBff.PayloadB);
        _delay.ReleaseAll();
        await Task.Yield();
        Assert.True(scanner.IsDetecting);
        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadB);
        await scanner.ConfirmCommand.ExecuteAsync(null);

        var first = _bff.Body(1).GetProperty("clientRequestId").GetGuid();
        var second = _bff.Body(3).GetProperty("clientRequestId").GetGuid();
        Assert.NotEqual(first, second);
        Assert.Equal(FakeQrIdentificationBff.PayloadB, _bff.Body(3).GetProperty("payload").GetString());
        Assert.Equal(QrScannerState.Completed, scanner.State);
    }

    [Fact]
    public async Task AnInvalidCodeIsTheSameMessageWhateverTheCause()
    {
        var scanner = await ScanningAsync();

        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadB);

        Assert.Equal(ParamedicMessages.QrInvalidOrExpired, scanner.StatusMessage);
        Assert.Equal(QrScannerState.Scanning, scanner.State);
        Assert.False(scanner.IsDetecting);
        Assert.Null(scanner.CandidateName);
        Assert.Null(scanner.ErrorMessage);

        // Sin bucle: no vuelve a detectar hasta la pausa.
        Assert.Equal(1, _bff.Resolves);
        Assert.Equal(1, _delay.Pending);
    }

    // ---- Errores ----

    [Fact]
    public async Task AlreadyIdentifiedIsReadAgainAndNeverReplaced()
    {
        _bff.IdentifiedElsewhere = true;
        var scanner = await ScanningAsync();

        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);

        Assert.Equal(1, _bff.EmergencyReads);
        Assert.Equal(QrScannerState.Completed, scanner.State);
        Assert.Equal(ParamedicMessages.QrAlreadyIdentified, scanner.StatusMessage);
        Assert.Null(scanner.CandidateName);
        Assert.Equal(0, _bff.Confirms);
        Assert.Empty(_navigator.Navigations);
    }

    [Fact]
    public async Task AConflictOnConfirmReadsTheEmergencyAgain()
    {
        var scanner = await CandidateAsync();
        _bff.IdentifiedElsewhere = true;

        await scanner.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(1, _bff.EmergencyReads);
        Assert.Equal(QrScannerState.Completed, scanner.State);
        Assert.Equal(ParamedicMessages.QrAlreadyIdentified, scanner.StatusMessage);
        Assert.Equal(0, _bff.IdentificationsCommitted);
        Assert.Equal(1, _bff.Confirms);
    }

    [Fact]
    public async Task AConflictWithoutPatientStopsWithAGenericMessage()
    {
        _bff.Closed = true;
        var scanner = await ScanningAsync();

        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);

        Assert.Equal(QrScannerState.Paused, scanner.State);
        Assert.Equal(ParamedicMessages.QrConflict, scanner.ErrorMessage);
        Assert.False(scanner.IsDetecting);
        Assert.Equal(1, _bff.Resolves);
    }

    [Fact]
    public async Task TooManyRequestsStopsTheScannerUntilThePersonResumesIt()
    {
        _bff.NextStatuses.Enqueue(HttpStatusCode.TooManyRequests);
        _bff.RetryAfter = TimeSpan.FromSeconds(30);
        var scanner = await ScanningAsync();

        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);

        Assert.Equal(QrScannerState.Paused, scanner.State);
        Assert.False(scanner.IsDetecting);
        Assert.False(scanner.IsCameraActive);
        Assert.Equal("Demasiados intentos. Espera 30 segundos e intenta nuevamente.", scanner.ErrorMessage);
        _delay.ReleaseAll();
        await Task.Yield();
        Assert.False(scanner.IsDetecting);
        Assert.Equal(1, _bff.Resolves);

        scanner.RescanCommand.Execute(null);
        Assert.Equal(QrScannerState.Scanning, scanner.State);
        Assert.True(scanner.IsDetecting);
        Assert.Null(scanner.ErrorMessage);
    }

    [Fact]
    public async Task TooManyRequestsOnConfirmKeepsTheCandidateAndTheRequestId()
    {
        var scanner = await CandidateAsync();
        _bff.NextStatuses.Enqueue(HttpStatusCode.TooManyRequests);

        await scanner.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(QrScannerState.Candidate, scanner.State);
        Assert.Equal(MobileApiErrorMessages.TooManyRequests, scanner.ErrorMessage);
        await scanner.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(_bff.Body(1).GetProperty("clientRequestId").GetGuid(), _bff.Body(2).GetProperty("clientRequestId").GetGuid());
        Assert.Equal(QrScannerState.Completed, scanner.State);
    }

    [Fact]
    public async Task ForbiddenShowsTheGenericMessage()
    {
        _bff.NextStatuses.Enqueue(HttpStatusCode.Forbidden);
        var scanner = await ScanningAsync();

        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);

        Assert.Equal(QrScannerState.Paused, scanner.State);
        Assert.Equal(MobileApiErrorMessages.Forbidden, scanner.ErrorMessage);
        Assert.Null(scanner.CandidateName);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ATemporaryErrorOnResolveCanBeRetriedByScanningAgain(HttpStatusCode status)
    {
        _bff.NextStatuses.Enqueue(status);
        var scanner = await ScanningAsync();

        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);

        Assert.Equal(QrScannerState.Paused, scanner.State);
        Assert.Equal(ParamedicMessages.QrResolveFailed, scanner.ErrorMessage);
        Assert.Null(scanner.CandidateName);

        scanner.RescanCommand.Execute(null);
        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);

        Assert.Equal(2, _bff.Resolves);
        Assert.Equal(QrScannerState.Candidate, scanner.State);
    }

    [Fact]
    public async Task AnExpiredAccessTokenIsRefreshedOnceByTheCommonHandler()
    {
        using var auth = new AuthHarness();
        await auth.SignInAsync();
        _bff.RequiredAccessToken = () => auth.Bff.AccessToken;
        var router = new RoutingHandler(auth.Bff, _bff);
        using var http = new HttpClient(new AuthenticatedHttpHandler(auth.Session, auth.Auth) { InnerHandler = router })
        {
            BaseAddress = new Uri("http://sciph.test/")
        };
        var scanner = new QrScannerViewModel(
            new HttpParamedicApiService(new MobileApiClient(http), _mock), _camera, auth.Session, _navigator, _delay.Delay)
        { EmergencyId = _emergencyId };
        await scanner.ActivateAsync();
        auth.Bff.ExpireAccessToken();

        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);

        Assert.Equal(1, auth.Bff.RefreshCalls);
        Assert.Equal(2, _bff.Resolves);
        Assert.Equal(_bff.Bodies[0], _bff.Bodies[1]);
        Assert.Equal(QrScannerState.Candidate, scanner.State);
    }

    // ---- Permiso de cámara ----

    [Fact]
    public async Task WithPermissionItScansWithoutAsking()
    {
        var scanner = await ScanningAsync();

        Assert.Equal(1, _camera.Checks);
        Assert.Equal(0, _camera.Requests);
        Assert.True(scanner.IsCameraActive);
    }

    [Fact]
    public async Task ThePermissionIsAskedOnceAndThenOnlyWhenThePersonTapsRetry()
    {
        _camera.CheckResult = CameraPermissionStatus.Denied;
        _camera.RequestResults.Enqueue(CameraPermissionStatus.Denied);
        var scanner = Scanner();

        await scanner.ActivateAsync();

        Assert.Equal(1, _camera.Requests);
        Assert.Equal(QrScannerState.PermissionDenied, scanner.State);
        Assert.Equal(ParamedicMessages.CameraPermissionDenied, scanner.StatusMessage);
        Assert.False(scanner.CanOpenSettings);
        Assert.False(scanner.IsCameraActive);

        // Volver a la página o a la app solo consulta: nunca vuelve a preguntar sola.
        await scanner.ResumeAsync();
        scanner.Deactivate();
        await scanner.ActivateAsync();
        Assert.Equal(1, _camera.Requests);

        _camera.RequestResults.Enqueue(CameraPermissionStatus.Granted);
        await scanner.RetryPermissionCommand.ExecuteAsync(null);

        Assert.Equal(2, _camera.Requests);
        Assert.Equal(QrScannerState.Scanning, scanner.State);
        Assert.True(scanner.IsDetecting);
        Assert.Empty(_bff.Paths);
    }

    [Fact]
    public async Task APermanentDenialOffersTheSettings()
    {
        _camera.CheckResult = CameraPermissionStatus.Denied;
        _camera.RequestResults.Enqueue(CameraPermissionStatus.DeniedPermanently);
        var scanner = Scanner();

        await scanner.ActivateAsync();

        Assert.Equal(QrScannerState.PermissionDenied, scanner.State);
        Assert.True(scanner.CanOpenSettings);
        Assert.Equal(ParamedicMessages.CameraPermissionDeniedPermanently, scanner.StatusMessage);
        scanner.OpenSettingsCommand.Execute(null);
        Assert.Equal(1, _camera.SettingsOpened);

        // Vuelve de la configuración con el permiso activado.
        _camera.CheckResult = CameraPermissionStatus.Granted;
        await scanner.ResumeAsync();
        Assert.Equal(QrScannerState.Scanning, scanner.State);
        Assert.Equal(1, _camera.Requests);
    }

    [Fact]
    public async Task WithoutACameraItSaysSoAndNeverAsks()
    {
        _camera.CheckResult = CameraPermissionStatus.Unavailable;
        var scanner = Scanner();

        await scanner.ActivateAsync();

        Assert.Equal(QrScannerState.Unavailable, scanner.State);
        Assert.Equal(ParamedicMessages.CameraUnavailable, scanner.StatusMessage);
        Assert.Equal(0, _camera.Requests);
        Assert.False(scanner.IsCameraActive);
    }

    // Encontrado con la cámara virtual: un código rechazado que sigue delante de la cámara se reenviaba cada 2 s hasta el 429.
    [Fact]
    public async Task ARejectedCodeStillInFrontOfTheCameraIsNotSentAgain()
    {
        var scanner = await ScanningAsync();
        var resolves = () => _bff.Paths.Count(p => p == ResolvePath);

        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadB);
        Assert.Equal(1, resolves());

        for (var i = 0; i < 5; i++)
        {
            _delay.ReleaseAll();
            Assert.True(scanner.IsDetecting);
            await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadB);
            Assert.Equal(1, resolves());
            Assert.Equal(ParamedicMessages.QrInvalidOrExpired, scanner.StatusMessage);
            Assert.Equal(QrScannerState.Scanning, scanner.State);
        }

        // El QR renovado es otro código: se envía en cuanto se lee.
        _delay.ReleaseAll();
        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);

        Assert.Equal(2, resolves());
        Assert.Equal(QrScannerState.Candidate, scanner.State);
    }

    [Fact]
    public async Task ARejectedCodeIsForgottenWithTheStaffSession()
    {
        var scanner = await ScanningAsync();
        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadB);
        _delay.ReleaseAll();

        StaffSession.SignIn(_session, Guid.NewGuid());
        await scanner.ActivateAsync();
        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadB);

        Assert.Equal(2, _bff.Paths.Count(p => p == ResolvePath));
    }

    [Fact]
    public async Task ATemporaryFailureIsNotARejectionAndTheSameCodeCanBeSentAgain()
    {
        var scanner = await ScanningAsync();
        _bff.NextStatuses.Enqueue(HttpStatusCode.ServiceUnavailable);

        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);
        Assert.Equal(QrScannerState.Paused, scanner.State);
        scanner.RescanCommand.Execute(null);
        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);

        Assert.Equal(QrScannerState.Candidate, scanner.State);
    }

    [Fact]
    public async Task APlatformFailureOnThePermissionIsUnavailableAndNeverACrash()
    {
        _camera.Throws = true;
        var scanner = Scanner();

        await scanner.ActivateAsync();

        Assert.Equal(QrScannerState.Unavailable, scanner.State);
        Assert.Equal(ParamedicMessages.CameraUnavailable, scanner.StatusMessage);
        Assert.False(scanner.IsCameraActive);
    }

    [Fact]
    public async Task WithoutAStaffSessionTheFlowCannotBeUsed()
    {
        var scanner = new QrScannerViewModel(
            new HttpParamedicApiService(_bff.Client(), _mock), _camera, new ClinicalSessionService(), _navigator, _delay.Delay)
        { EmergencyId = _emergencyId };

        await scanner.ActivateAsync();
        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);

        Assert.Equal(QrScannerState.Unavailable, scanner.State);
        Assert.False(scanner.IsCameraActive);
        Assert.Equal(0, _camera.Checks + _camera.Requests);
        Assert.Empty(_bff.Paths);
    }

    [Fact]
    public async Task AFailedNavigationAfterConfirmingIsNotAFailedConfirmation()
    {
        var scanner = await CandidateAsync();
        _navigator.GoToException = new InvalidOperationException("navigation");

        await scanner.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(QrScannerState.Completed, scanner.State);
        Assert.Null(scanner.ErrorMessage);
        Assert.False(scanner.ShowCancel);
        Assert.Equal(1, _bff.Confirms);
    }

    [Fact]
    public async Task AlreadyIdentifiedOffersOnlyTheWayBack()
    {
        var scanner = await ScanningAsync();
        Assert.True(scanner.ShowCancel);
        _bff.IdentifiedElsewhere = true;

        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);

        Assert.Equal(QrScannerState.Completed, scanner.State);
        Assert.False(scanner.ShowCancel);
    }

    [Fact]
    public async Task WhereTheScannerIsNotOfferedTheRouteIsSafe()
    {
        _camera.IsScannerSupported = false;
        var scanner = Scanner();

        await scanner.ActivateAsync();

        Assert.Equal(QrScannerState.Unavailable, scanner.State);
        Assert.Equal(ParamedicMessages.QrScannerUnsupported, scanner.StatusMessage);
        Assert.Equal(0, _camera.Checks + _camera.Requests);
        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);
        Assert.Empty(_bff.Paths);
        Assert.True(scanner.CancelCommand.CanExecute(null));
    }

    // ---- Ciclo de vida y sesión ----

    [Fact]
    public async Task LeavingThePageStopsDetectionAndDropsALateCandidate()
    {
        var scanner = await ScanningAsync();
        var resolveGate = _bff.ResolveGate = new TaskCompletionSource();
        var resolving = scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);
        Assert.Equal(QrScannerState.Resolving, scanner.State);

        scanner.Deactivate();
        resolveGate.SetResult();
        await resolving;

        Assert.False(scanner.IsDetecting);
        Assert.Null(scanner.CandidateName);
        Assert.NotEqual(QrScannerState.Candidate, scanner.State);
        Assert.False(scanner.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task ALateCandidateOfAnotherStaffSessionIsDropped()
    {
        var scanner = await ScanningAsync();
        var resolveGate = _bff.ResolveGate = new TaskCompletionSource();
        var resolving = scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);

        StaffSession.SignIn(_session, Guid.NewGuid());
        resolveGate.SetResult();
        await resolving;

        Assert.Null(scanner.CandidateName);
        Assert.Equal(QrScannerState.Permission, scanner.State);
        Assert.False(scanner.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task StaffBNeverSeesStaffAsCandidate()
    {
        var scanner = await CandidateAsync();
        Assert.Equal("Ana Pérez", scanner.CandidateName);

        _session.Clear();
        Assert.Null(scanner.CandidateName);
        Assert.Null(scanner.CandidateBirthDate);
        Assert.Null(scanner.CandidateDocument);
        Assert.False(scanner.ConfirmCommand.CanExecute(null));
        Assert.False(scanner.IsDetecting);

        StaffSession.SignIn(_session, Guid.NewGuid());
        await scanner.ActivateAsync();

        Assert.Null(scanner.CandidateName);
        Assert.Null(scanner.ErrorMessage);
        Assert.Equal(QrScannerState.Scanning, scanner.State);
        await scanner.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(0, _bff.Confirms);
    }

    [Fact]
    public async Task AConfirmationAnsweredAfterLeavingIsRecoveredWithTheSameRequestId()
    {
        var scanner = await CandidateAsync();
        var confirmGate = _bff.ConfirmGate = new TaskCompletionSource();
        var confirming = scanner.ConfirmCommand.ExecuteAsync(null);

        scanner.Deactivate();
        confirmGate.SetResult();
        await confirming;

        // La respuesta llegó tarde y se descartó, pero el servidor identificó: volver a confirmar no identifica dos veces.
        Assert.Empty(_navigator.Navigations);
        await scanner.ActivateAsync();
        Assert.Equal(QrScannerState.Candidate, scanner.State);
        await scanner.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(_bff.Body(1).GetProperty("clientRequestId").GetGuid(), _bff.Body(2).GetProperty("clientRequestId").GetGuid());
        Assert.Equal(1, _bff.IdentificationsCommitted);
        Assert.Equal(QrScannerState.Completed, scanner.State);
    }

    [Fact]
    public async Task ScanningAgainForgetsTheCandidate()
    {
        var scanner = await CandidateAsync();

        scanner.RescanCommand.Execute(null);

        Assert.Equal(QrScannerState.Scanning, scanner.State);
        Assert.True(scanner.IsDetecting);
        Assert.Null(scanner.CandidateName);
        Assert.False(scanner.ConfirmCommand.CanExecute(null));
        Assert.Equal(0, _bff.Confirms);
    }

    /// <summary>Envía la autenticación al BFF falso de cuentas y el resto al de QR.</summary>
    private sealed class RoutingHandler(FakeMobileBff auth, FakeQrIdentificationBff qr) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _auth = new(auth, disposeHandler: false);
        private readonly HttpMessageInvoker _qr = new(qr, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.RequestUri!.AbsolutePath.StartsWith("/api/mobile/auth/", StringComparison.Ordinal)
                ? _auth.SendAsync(request, cancellationToken)
                : _qr.SendAsync(request, cancellationToken);
    }
}

// La tercera vía de la pantalla de identificación: solo abre el escáner, y al volver la emergencia se lee del servidor.
public class IdentifyWithQrTests
{
    private readonly FakeQrIdentificationBff _bff = new();
    private readonly MockParamedicApiService _mock = new();
    private readonly FakeNavigator _navigator = new();
    private readonly Guid _emergencyId = Guid.NewGuid();

    private IdentifyPatientViewModel Screen(bool scannerSupported = true) =>
        new(new HttpParamedicApiService(_bff.Client(), _mock), new CountingFingerprintService(), _navigator,
            camera: new FakeCameraPermission { IsScannerSupported = scannerSupported })
        { EmergencyId = _emergencyId };

    [Fact]
    public async Task TheQrOptionOpensTheScannerForTheSameEmergency()
    {
        var screen = Screen();
        Assert.True(screen.ShowQrOption);
        Assert.True(screen.CanIdentify);

        await screen.ScanQrCommand.ExecuteAsync(null);

        var navigation = Assert.Single(_navigator.Navigations);
        Assert.Equal(ParamedicRoutes.ScanQr, navigation.Route);
        Assert.Equal(_emergencyId, navigation.Parameters!["EmergencyId"]);
        Assert.Empty(_bff.Paths);
    }

    [Fact]
    public void TheQrOptionIsHiddenWhereTheScannerIsNotOffered()
    {
        var screen = Screen(scannerSupported: false);

        Assert.False(screen.ShowQrOption);
        Assert.False(screen.ScanQrCommand.CanExecute(null));
        Assert.True(screen.CanIdentify);
    }

    [Fact]
    public async Task BackFromTheScannerTheServerSaysWhetherItIsIdentified()
    {
        var screen = Screen();
        await screen.ScanQrCommand.ExecuteAsync(null);

        // Volvió sin confirmar: sigue NN y con las tres vías.
        await screen.RefreshAfterQrScannerAsync();
        Assert.False(screen.IsIdentified);
        Assert.True(screen.ShowQrOption);
        Assert.Equal(1, _bff.EmergencyReads);

        await screen.ScanQrCommand.ExecuteAsync(null);
        var scanner = new QrScannerViewModel(
            new HttpParamedicApiService(_bff.Client(), _mock), new FakeCameraPermission(), StaffSession.SignedIn(), _navigator)
        { EmergencyId = _emergencyId };
        await scanner.ActivateAsync();
        await scanner.ProcessScanAsync(FakeQrIdentificationBff.PayloadA);
        await scanner.ConfirmCommand.ExecuteAsync(null);

        await screen.RefreshAfterQrScannerAsync();

        Assert.True(screen.IsIdentified);
        Assert.Equal("Ana Pérez", screen.PatientDisplayName);
        Assert.False(screen.ShowQrOption);
        Assert.False(screen.CanIdentify);
        // La misma emergencia, con su código NN: nada se recrea.
        var emergency = await _mock.GetEmergencyAsync(_emergencyId);
        Assert.Equal(FakeQrIdentificationBff.TemporaryCode, emergency!.TemporaryPatientCode);
        Assert.Equal(_emergencyId, emergency.Id);
        Assert.DoesNotContain("POST /api/mobile/emergencies", _bff.Paths);
    }

    [Fact]
    public async Task WithoutOpeningTheScannerNothingIsRead()
    {
        var screen = Screen();

        await screen.RefreshAfterQrScannerAsync();

        Assert.Empty(_bff.Paths);
        Assert.False(screen.IsIdentified);
    }
}
