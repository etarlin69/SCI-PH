using System.Net;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Features.Patient;
using SCI_PH.Mobile.Features.Patient.Services;
using SCI_PH.Mobile.Features.Patient.ViewModels;
using SCI_PH.Mobile.Tests.Auth;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace SCI_PH.Mobile.Tests.Patient;

// "Mi QR": solo el contenido real del servidor, solo en memoria, y nunca de otra cuenta ni vencido.
public class MyQrTests
{
    private const string PayloadA = "sciphqr:v1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string PayloadB = "sciphqr:v1:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBA";
    private const string Route = $"{FakePatientSelfBff.Me}/qr";

    private readonly FakePatientSelfBff _bff = new();
    private readonly ClinicalSessionService _session = PatientSession.SignedIn();
    private readonly TestClock _clock = new();

    public MyQrTests() => Respond(PayloadA);

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    // Las horas del servidor no coinciden con el reloj del dispositivo a propósito.
    private void Respond(string payload) =>
        _bff.Responses[Route] = $$"""{"payload":"{{payload}}","issuedAtUtc":"2020-01-01T00:00:00Z","expiresAtUtc":"2020-01-01T00:15:00Z"}""";

    private MyQrViewModel Screen(ClinicalSessionService? session = null) =>
        new(new HttpPatientQrService(_bff.Client()), session ?? _session, _clock);

    private int IssueCalls => _bff.Requests.Count(r => r.Path == Route);

    [Fact]
    public async Task ItShowsTheRealPayloadRequestedWithoutAnyPatientId()
    {
        var screen = Screen();
        var loading = screen.LoadCommand.ExecuteAsync(null);
        await loading;

        Assert.True(screen.HasQr);
        Assert.Equal(PayloadA, screen.Payload);
        Assert.Equal("Caduca en 15:00", screen.ExpiresIn);
        Assert.False(screen.HasError);
        var request = Assert.Single(_bff.Requests);
        Assert.Equal((HttpMethod.Post, Route, false), request);
    }

    [Fact]
    public async Task WhileLoadingThereIsNoQr()
    {
        var gate = new TaskCompletionSource();
        _bff.Gate = gate;
        var screen = Screen();

        var loading = screen.LoadCommand.ExecuteAsync(null);

        Assert.True(screen.IsBusy);
        Assert.False(screen.HasQr);
        gate.SetResult();
        await loading;
        Assert.False(screen.IsBusy);
        Assert.True(screen.HasQr);
    }

    [Fact]
    public async Task ComingBackToTheScreenKeepsTheValidQrInMemory()
    {
        var screen = Screen();
        await screen.LoadCommand.ExecuteAsync(null);
        _clock.Now += TimeSpan.FromMinutes(5);

        await screen.LoadCommand.ExecuteAsync(null);

        Assert.Equal(PayloadA, screen.Payload);
        Assert.Equal("Caduca en 10:00", screen.ExpiresIn);
        Assert.Equal(1, IssueCalls);
    }

    [Fact]
    public async Task AReopenedAppRequestsANewQrBecauseNothingWasStored()
    {
        await Screen().LoadCommand.ExecuteAsync(null);
        Respond(PayloadB);

        var reopened = Screen();
        await reopened.LoadCommand.ExecuteAsync(null);

        Assert.Equal(PayloadB, reopened.Payload);
        Assert.Equal(2, IssueCalls);
    }

    [Fact]
    public async Task TheCountdownFollowsTheDeviceAndAnExpiredQrIsReplaced()
    {
        var screen = Screen();
        await screen.LoadCommand.ExecuteAsync(null);

        _clock.Now += TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(30);
        await screen.TickAsync();
        Assert.Equal("Caduca en 00:30", screen.ExpiresIn);
        Assert.Equal(1, IssueCalls);

        Respond(PayloadB);
        var gate = new TaskCompletionSource();
        _bff.Gate = gate;
        _clock.Now += TimeSpan.FromSeconds(30);
        var renewing = screen.TickAsync();

        // Vencido: desaparece de inmediato, antes de que llegue el nuevo.
        Assert.True(screen.IsRenewing);
        Assert.False(screen.HasQr);
        gate.SetResult();
        await renewing;

        Assert.False(screen.IsRenewing);
        Assert.Equal(PayloadB, screen.Payload);
        Assert.Equal("Caduca en 15:00", screen.ExpiresIn);
    }

    [Fact]
    public async Task AnExpiredQrIsNeverShownWithoutConnection()
    {
        var screen = Screen();
        await screen.LoadCommand.ExecuteAsync(null);
        _bff.Unreachable = true;
        _clock.Now += TimeSpan.FromMinutes(15);

        await screen.TickAsync();

        Assert.False(screen.HasQr);
        Assert.Null(screen.ExpiresIn);
        Assert.Equal(MobileApiErrorMessages.Network, screen.ErrorMessage);

        // No se reintenta solo cada segundo: el reintento es del paciente.
        var calls = _bff.Requests.Count;
        await screen.TickAsync();
        Assert.Equal(calls, _bff.Requests.Count);

        _bff.Unreachable = false;
        await screen.RetryCommand.ExecuteAsync(null);
        Assert.True(screen.HasQr);
        Assert.False(screen.HasError);
    }

    [Fact]
    public async Task ReopeningAnExpiredQrWithoutConnectionShowsTheNetworkState()
    {
        var screen = Screen();
        await screen.LoadCommand.ExecuteAsync(null);
        _bff.Unreachable = true;
        _clock.Now += TimeSpan.FromMinutes(16);

        await screen.LoadCommand.ExecuteAsync(null);

        Assert.Null(screen.Payload);
        Assert.Equal(MobileApiErrorMessages.Network, screen.ErrorMessage);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, PatientMessages.SessionEnded)]
    [InlineData(HttpStatusCode.Forbidden, PatientMessages.NoRecordAccess)]
    [InlineData(HttpStatusCode.ServiceUnavailable, MobileApiErrorMessages.Server)]
    [InlineData(HttpStatusCode.TooManyRequests, MobileApiErrorMessages.TooManyRequests)]
    public async Task EveryFailureLeavesAnEmptyScreenWithItsMessage(HttpStatusCode status, string message)
    {
        _bff.Status = status;
        var screen = Screen();

        await screen.LoadCommand.ExecuteAsync(null);

        Assert.False(screen.HasQr);
        Assert.Null(screen.ExpiresIn);
        Assert.Equal(message, screen.ErrorMessage);
    }

    [Fact]
    public async Task AConcurrentIssueIsRetriedOnce()
    {
        _bff.NextStatuses.Enqueue(HttpStatusCode.Conflict);
        var screen = Screen();

        await screen.LoadCommand.ExecuteAsync(null);

        Assert.Equal(PayloadA, screen.Payload);
        Assert.Equal(2, IssueCalls);
    }

    [Fact]
    public async Task ASecondConflictIsAnErrorAndNotALoop()
    {
        _bff.Status = HttpStatusCode.Conflict;
        var screen = Screen();

        await screen.LoadCommand.ExecuteAsync(null);

        Assert.False(screen.HasQr);
        Assert.Equal(MobileApiErrorMessages.Conflict, screen.ErrorMessage);
        Assert.Equal(2, IssueCalls);
    }

    [Theory]
    [InlineData("""{"payload":"not-a-qr","issuedAtUtc":"2020-01-01T00:00:00Z","expiresAtUtc":"2020-01-01T00:15:00Z"}""")]
    [InlineData("""{"payload":"sciphqr:v1:AAAA","issuedAtUtc":"2020-01-01T00:15:00Z","expiresAtUtc":"2020-01-01T00:00:00Z"}""")]
    public async Task AnUnexpectedResponseIsNeverDrawn(string response)
    {
        _bff.Responses[Route] = response;
        var screen = Screen();

        await screen.LoadCommand.ExecuteAsync(null);

        Assert.False(screen.HasQr);
        Assert.True(screen.HasError);
    }

    [Fact]
    public async Task ClosingTheSessionRemovesThePayloadFromMemoryAtOnce()
    {
        var screen = Screen();
        await screen.LoadCommand.ExecuteAsync(null);

        _session.Clear();

        Assert.Null(screen.Payload);
        Assert.Null(screen.ExpiresIn);
    }

    [Fact]
    public async Task RealLogoutClearsTheQrWithoutRevokingItOnTheServer()
    {
        using var auth = new AuthHarness();
        auth.Bff.Roles = [AccountRoles.Patient];
        auth.Bff.Permissions = [];
        await auth.SignInAsync();
        var screen = new MyQrViewModel(new HttpPatientQrService(_bff.Client()), auth.Session, _clock);
        await screen.LoadCommand.ExecuteAsync(null);
        Assert.True(screen.HasQr);

        await auth.Auth.LogoutAsync();

        Assert.Null(screen.Payload);
        Assert.DoesNotContain(_bff.Requests, r => r.Method == HttpMethod.Delete);
        Assert.Empty(auth.Storage.Values);
    }

    [Fact]
    public async Task PatientBNeverSeesPatientAsQr()
    {
        var screen = Screen();
        await screen.LoadCommand.ExecuteAsync(null);
        Assert.Equal(PayloadA, screen.Payload);

        _session.Clear();
        PatientSession.SignIn(_session, Guid.NewGuid());
        Assert.Null(screen.Payload);

        Respond(PayloadB);
        await screen.LoadCommand.ExecuteAsync(null);

        Assert.Equal(PayloadB, screen.Payload);
        Assert.Equal(2, IssueCalls);
    }

    [Fact]
    public async Task ALateResponseForPatientAIsDiscardedAfterPatientBSignsIn()
    {
        var gate = new TaskCompletionSource();
        _bff.Gate = gate;
        var screen = Screen();
        var loading = screen.LoadCommand.ExecuteAsync(null);

        _session.Clear();
        PatientSession.SignIn(_session, Guid.NewGuid());
        gate.SetResult();
        await loading;

        Assert.Null(screen.Payload);
        Assert.False(screen.HasError);
    }

    [Fact]
    public async Task ALateRenewalForPatientADoesNotReachPatientB()
    {
        var screen = Screen();
        await screen.LoadCommand.ExecuteAsync(null);
        var gate = new TaskCompletionSource();
        _bff.Gate = gate;
        _clock.Now += TimeSpan.FromMinutes(15);
        var renewing = screen.TickAsync();

        _session.Clear();
        PatientSession.SignIn(_session, Guid.NewGuid());
        gate.SetResult();
        await renewing;

        Assert.Null(screen.Payload);
        Assert.False(screen.IsRenewing);
    }

    [Fact]
    public void ThePayloadNeverAppearsInTheTextOfItsModel() =>
        Assert.DoesNotContain(PayloadA, new Features.Patient.Models.Dtos.PatientQrDto(PayloadA, TimeSpan.FromMinutes(15)).ToString());

    [Fact]
    public void ThePayloadFitsAndReadsBackFromAQrCode()
    {
        // El mismo codificador que usa la vista de la app.
        var matrix = new QRCodeWriter().encode(PayloadA, BarcodeFormat.QR_CODE, 300, 300);
        var luminance = new byte[matrix.Width * matrix.Height];
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
            {
                luminance[(y * matrix.Width) + x] = matrix[x, y] ? (byte)0 : (byte)255;
            }
        }

        var bitmap = new BinaryBitmap(new HybridBinarizer(new PlanarYUVLuminanceSource(
            luminance, matrix.Width, matrix.Height, 0, 0, matrix.Width, matrix.Height, false)));

        Assert.Equal(PayloadA, new QRCodeReader().decode(bitmap)?.Text);
    }
}

// La pantalla y su servicio no persisten ni registran el contenido, y dibujar el QR no activa ni cámara ni lector.
public class MyQrSurfaceTests
{
    private static string AppFile(params string[] relativePath)
    {
        var relative = Path.Combine(relativePath);
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "SCI-PH.Mobile", relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(relative);
    }

    [Theory]
    [InlineData("ViewModels", "MyQrViewModel.cs")]
    [InlineData("Services", "HttpPatientQrService.cs")]
    [InlineData("Qr", "MyQrPage.xaml.cs")]
    public void NothingOfTheQrIsPersistedOrLogged(string folder, string file)
    {
        var source = File.ReadAllText(AppFile("Features", "Patient", folder, file));

        foreach (var forbidden in new[]
        {
            "Preferences.", "IPreferences", "SecureStorage", "ISecureTokenStore", "SQLite", "File.", "FileSystem",
            "ILogger", "Debug.Write", "Console.Write", "Trace."
        })
        {
            Assert.DoesNotContain(forbidden, source);
        }
    }

    [Fact]
    public void ThereIsNoSamplePayloadInTheApp()
    {
        Assert.DoesNotContain("sciphqr:v1:", File.ReadAllText(AppFile("Features", "Patient", "ViewModels", "MyQrViewModel.cs")));
        Assert.DoesNotContain("Value=", File.ReadAllText(AppFile("Features", "Patient", "Qr", "MyQrPage.xaml")));
    }

    [Fact]
    public void DrawingTheQrAsksForNoCameraOrScanner()
    {
        var program = File.ReadAllText(AppFile("MauiProgram.cs"));
        var page = File.ReadAllText(AppFile("Features", "Patient", "Qr", "MyQrPage.xaml.cs"));

        Assert.Contains("BarcodeGeneratorViewHandler", program);
        Assert.DoesNotContain(".UseBarcodeReader", program);
        Assert.DoesNotContain("CameraBarcodeReader", program);
        Assert.DoesNotContain("CameraBarcodeReader", page);
        Assert.DoesNotContain("android.permission.CAMERA", File.ReadAllText(AppFile("Platforms", "Android", "AndroidManifest.xml")));
        Assert.DoesNotContain("NSCameraUsageDescription", File.ReadAllText(AppFile("Platforms", "iOS", "Info.plist")));
    }

    [Fact]
    public void TheScreenExplainsIdentificationAndNeverSignIn()
    {
        var page = File.ReadAllText(AppFile("Features", "Patient", "Qr", "MyQrPage.xaml"));

        Assert.Contains("Este código identifica tu expediente al personal autorizado.", page);
        foreach (var wrong in new[] { "iniciar sesión", "Iniciar sesión", "contraseña", "acceso automático" })
        {
            Assert.DoesNotContain(wrong, page);
        }
    }
}
