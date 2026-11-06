using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

/// <summary>Estados del escáner de QR del paramédico.</summary>
public enum QrScannerState
{
    /// <summary>Comprobando o pidiendo el permiso de cámara.</summary>
    Permission,
    PermissionDenied,

    /// <summary>Cámara activa (la detección puede estar en espera, ver <see cref="QrScannerViewModel.IsDetecting"/>).</summary>
    Scanning,
    Resolving,

    /// <summary>Candidato en pantalla: nada se identifica sin "Confirmar paciente".</summary>
    Candidate,
    Confirming,

    /// <summary>Un error detuvo el escáner: solo se reanuda cuando el paramédico lo pide.</summary>
    Paused,
    Unavailable,
    Completed
}

/// <summary>
/// Identificación con el QR del paciente (ADR-015), en una página propia sobre la de identificación. Escanear solo pide el
/// candidato al servidor (resolve), que nunca identifica; la emergencia se identifica únicamente cuando el paramédico pulsa
/// "Confirmar paciente" (confirm), y el servidor vuelve a validar el QR. Sin ZXing aquí: la página entrega el contenido leído
/// (<see cref="ProcessScanAsync"/>) y aplica <see cref="IsDetecting"/> a la cámara.
/// <list type="bullet">
/// <item>Una lectura aceptada detiene la detección en el acto: una sola petición por lectura.</item>
/// <item>El contenido no se recorta ni se modifica, solo vive en memoria mientras hay candidato y nunca se registra.</item>
/// <item>La clave de la confirmación se genera una vez por candidato y se conserva en los reintentos (sin respuesta, 503, 429):
/// repetirla nunca identifica dos veces.</item>
/// <item>Un cambio de sesión o salir de la página descarta las respuestas tardías; el resultado de una confirmación ya enviada
/// lo da el servidor (la identificación vuelve a leer la emergencia).</item>
/// </list>
/// </summary>
[QueryProperty(nameof(EmergencyId), "EmergencyId")]
public sealed partial class QrScannerViewModel : BaseViewModel
{
    public const string PayloadPrefix = "sciphqr:v1:";

    /// <summary>Espera antes de volver a detectar tras un código no válido (el mismo código sigue delante de la cámara).</summary>
    public static readonly TimeSpan RearmDelay = TimeSpan.FromSeconds(2);

    private readonly IParamedicApiService _api;
    private readonly ICameraPermissionService _camera;
    private readonly IClinicalSessionService _session;
    private readonly IAppNavigator _navigator;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    private Guid? _owner;
    private int _generation;
    private bool _subscribed;
    private bool _requestedPermission;
    private CancellationTokenSource? _visit;
    private CancellationTokenSource? _rearm;

    // El QR del candidato (solo en memoria) y la clave de SU confirmación.
    private string? _payload;
    private Guid? _confirmRequestId;

    public QrScannerViewModel(
        IParamedicApiService api,
        ICameraPermissionService camera,
        IClinicalSessionService session,
        IAppNavigator navigator,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _api = api;
        _camera = camera;
        _session = session;
        _navigator = navigator;
        _delay = delay ?? Task.Delay;
    }

    [ObservableProperty]
    private Guid emergencyId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCameraActive))]
    [NotifyPropertyChangedFor(nameof(IsWorking))]
    [NotifyPropertyChangedFor(nameof(IsPermissionDenied))]
    [NotifyPropertyChangedFor(nameof(HasCandidate))]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    [NotifyPropertyChangedFor(nameof(IsUnavailable))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(RescanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryPermissionCommand))]
    private QrScannerState state = QrScannerState.Permission;

    /// <summary>La cámara acepta una lectura ahora mismo. La página lo aplica a la vista de ZXing.</summary>
    [ObservableProperty]
    private bool isDetecting;

    [ObservableProperty]
    private bool canOpenSettings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? statusMessage;

    [ObservableProperty]
    private string? candidateName;

    [ObservableProperty]
    private string? candidateBirthDate;

    [ObservableProperty]
    private string? candidateDocument;

    /// <summary>La vista de la cámara existe solo en este estado (en el candidato y al confirmar, no).</summary>
    public bool IsCameraActive => State == QrScannerState.Scanning;

    public bool IsWorking => State is QrScannerState.Permission or QrScannerState.Resolving or QrScannerState.Confirming;

    public bool IsPermissionDenied => State == QrScannerState.PermissionDenied;

    public bool HasCandidate => State is QrScannerState.Candidate or QrScannerState.Confirming;

    public bool IsPaused => State == QrScannerState.Paused;

    public bool IsUnavailable => State == QrScannerState.Unavailable;

    public bool IsCompleted => State == QrScannerState.Completed;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    // ---- Ciclo de vida (la página) ----

    /// <summary>La página se ve: comprueba la sesión y el permiso y, si procede, activa la detección.</summary>
    public async Task ActivateAsync()
    {
        if (!_subscribed)
        {
            _session.SessionChanged += OnSessionChanged;
            _subscribed = true;
        }

        _visit?.Dispose();
        _visit = new CancellationTokenSource();

        var user = _session.CurrentUser?.Id;
        if (_owner is null || _owner != user)
        {
            ResetForSession(user);
        }

        if (!_camera.IsScannerSupported)
        {
            State = QrScannerState.Unavailable;
            StatusMessage = ParamedicMessages.QrScannerUnsupported;
            return;
        }

        if (user is null)
        {
            State = QrScannerState.Unavailable;
            ErrorMessage = MobileApiErrorMessages.Unauthorized;
            return;
        }

        switch (State)
        {
            case QrScannerState.Permission:
                await EnsurePermissionAsync(allowRequest: !_requestedPermission);
                break;
            case QrScannerState.PermissionDenied:
                await RecheckPermissionAsync();
                break;
            case QrScannerState.Scanning:
                StartScanning(delayed: false);
                break;
            case QrScannerState.Confirming:
                // La respuesta de la confirmación se descartó al salir: se puede repetir con la MISMA clave.
                State = QrScannerState.Candidate;
                ErrorMessage = ParamedicMessages.QrConfirmFailed;
                break;
        }
    }

    /// <summary>La página dejó de verse: detección apagada, lectura en curso cancelada y respuestas tardías descartadas.</summary>
    public void Deactivate()
    {
        if (_subscribed)
        {
            _session.SessionChanged -= OnSessionChanged;
            _subscribed = false;
        }

        IsDetecting = false;
        Interlocked.Increment(ref _generation);
        _visit?.Cancel();
        _rearm?.Cancel();
        if (State == QrScannerState.Resolving)
        {
            Discard();
            State = QrScannerState.Scanning;
        }
    }

    /// <summary>La app vuelve a primer plano con la página visible: vuelve a mirar el permiso (sin pedirlo).</summary>
    public async Task ResumeAsync()
    {
        if (State == QrScannerState.PermissionDenied)
        {
            await RecheckPermissionAsync();
        }
    }

    // ---- Permiso ----

    private async Task EnsurePermissionAsync(bool allowRequest)
    {
        var generation = _generation;
        State = QrScannerState.Permission;
        var status = await _camera.CheckAsync();
        if (status == CameraPermissionStatus.Denied && allowRequest)
        {
            // La única petición sin que el paramédico la pida: al entrar por primera vez.
            _requestedPermission = true;
            status = await _camera.RequestAsync();
        }

        if (generation == _generation)
        {
            ApplyPermission(status);
        }
    }

    private async Task RecheckPermissionAsync()
    {
        var generation = _generation;
        var status = await _camera.CheckAsync();
        if (generation == _generation && State == QrScannerState.PermissionDenied && status != CameraPermissionStatus.Denied)
        {
            ApplyPermission(status);
        }
    }

    private bool CanRetryPermission() => State == QrScannerState.PermissionDenied;

    // "Intentar de nuevo": solo así se vuelve a pedir el permiso (nunca en bucle).
    [RelayCommand(CanExecute = nameof(CanRetryPermission))]
    private async Task RetryPermissionAsync()
    {
        var generation = _generation;
        _requestedPermission = true;
        var status = await _camera.RequestAsync();
        if (generation == _generation)
        {
            ApplyPermission(status);
        }
    }

    [RelayCommand]
    private void OpenSettings() => _camera.OpenSettings();

    private void ApplyPermission(CameraPermissionStatus status)
    {
        switch (status)
        {
            case CameraPermissionStatus.Granted:
                StatusMessage = null;
                CanOpenSettings = false;
                StartScanning(delayed: false);
                break;
            case CameraPermissionStatus.Denied:
                CanOpenSettings = false;
                StatusMessage = ParamedicMessages.CameraPermissionDenied;
                State = QrScannerState.PermissionDenied;
                break;
            case CameraPermissionStatus.DeniedPermanently:
                CanOpenSettings = true;
                StatusMessage = ParamedicMessages.CameraPermissionDeniedPermanently;
                State = QrScannerState.PermissionDenied;
                break;
            default:
                CanOpenSettings = false;
                StatusMessage = ParamedicMessages.CameraUnavailable;
                State = QrScannerState.Unavailable;
                break;
        }
    }

    // ---- Lectura ----

    /// <summary>
    /// Contenido de un QR leído por la cámara (ya en el hilo de la interfaz). Solo se procesa uno con la detección activa; un
    /// código que no es de SCI-PH no llega al servidor.
    /// </summary>
    public async Task ProcessScanAsync(string value)
    {
        if (State != QrScannerState.Scanning || !IsDetecting)
        {
            return;
        }

        IsDetecting = false;
        if (!value.StartsWith(PayloadPrefix, StringComparison.Ordinal))
        {
            ErrorMessage = null;
            StatusMessage = ParamedicMessages.QrInvalidCode;
            StartScanning(delayed: true);
            return;
        }

        var generation = _generation;
        var token = _visit?.Token ?? CancellationToken.None;
        _payload = value;
        _confirmRequestId = null;
        ErrorMessage = null;
        StatusMessage = null;
        State = QrScannerState.Resolving;
        try
        {
            var candidate = await _api.ResolveQrCandidateAsync(EmergencyId, value, token);
            if (!IsCurrent(generation))
            {
                return;
            }

            ShowCandidate(candidate);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception) when (!IsCurrent(generation))
        {
            // Respuesta de una visita o una sesión anterior: no es de esta pantalla.
        }
        catch (MobileApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            RestartAfterInvalidCode();
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.Conflict)
        {
            await ResolveConflictAsync(generation);
        }
        catch (MobileApiException ex) when (ex.Error is MobileApiError.TooManyRequests or MobileApiError.Forbidden or MobileApiError.Unauthorized)
        {
            Pause(MobileApiErrorMessages.For(ex));
        }
        catch (Exception)
        {
            Pause(ParamedicMessages.QrResolveFailed);
        }
    }

    // ---- Confirmación ----

    private bool CanConfirm() => State == QrScannerState.Candidate;

    // REAL: identifica la MISMA emergencia. Nunca se llama sola tras leer: solo con "Confirmar paciente".
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (State != QrScannerState.Candidate || _payload is not { } payload)
        {
            return;
        }

        var generation = _generation;
        _confirmRequestId ??= Guid.NewGuid();
        var clientRequestId = _confirmRequestId.Value;
        ErrorMessage = null;
        State = QrScannerState.Confirming;
        try
        {
            // Sin cancelar al salir: una confirmación enviada puede haberse aplicado; su resultado lo da el servidor.
            await _api.ConfirmQrIdentificationAsync(EmergencyId, clientRequestId, payload);
            if (!IsCurrent(generation))
            {
                return;
            }

            Discard();
            State = QrScannerState.Completed;
            await _navigator.GoToAsync("..");
        }
        catch (Exception) when (!IsCurrent(generation))
        {
        }
        catch (MobileApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // El QR venció o se sustituyó mientras tanto: nunca se confía en el candidato anterior.
            RestartAfterInvalidCode();
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.Conflict)
        {
            await ResolveConflictAsync(generation);
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.TooManyRequests)
        {
            // Rechazada antes de procesarse: el mismo candidato y la misma clave, reintento manual.
            State = QrScannerState.Candidate;
            ErrorMessage = MobileApiErrorMessages.For(ex);
        }
        catch (MobileApiException ex) when (ex.Error is MobileApiError.Forbidden or MobileApiError.Unauthorized)
        {
            Pause(MobileApiErrorMessages.For(ex));
        }
        catch (Exception)
        {
            // Sin respuesta, 503 o respuesta inesperada: pudo aplicarse. El reintento lleva la MISMA clave.
            State = QrScannerState.Candidate;
            ErrorMessage = ParamedicMessages.QrConfirmFailed;
        }
    }

    private bool CanRescan() => State is QrScannerState.Candidate or QrScannerState.Paused;

    [RelayCommand(CanExecute = nameof(CanRescan))]
    private void Rescan()
    {
        Discard();
        ErrorMessage = null;
        StatusMessage = null;
        StartScanning(delayed: false);
    }

    private bool CanCancel() => State != QrScannerState.Confirming;

    // Salir sin confirmar no cambia nada en la emergencia.
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync()
    {
        Discard();
        IsDetecting = false;
        await _navigator.GoToAsync("..");
    }

    // ---- Helpers ----

    private void ShowCandidate(QrCandidateDto candidate)
    {
        CandidateName = candidate.DisplayName;
        CandidateBirthDate = candidate.DateOfBirth?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        CandidateDocument = candidate.DocumentValue is { } value ? $"{DocumentLabel(candidate.DocumentType)} {value}" : null;
        State = QrScannerState.Candidate;
    }

    private static string DocumentLabel(string? type) => type switch
    {
        "NationalId" => "Cédula",
        "Passport" => "Pasaporte",
        _ => "Documento"
    };

    // 409: la emergencia se vuelve a leer. Con paciente, nunca se sustituye; sin él, conflicto genérico.
    private async Task ResolveConflictAsync(int generation)
    {
        Discard();
        EmergencyDto? emergency = null;
        try
        {
            emergency = await _api.GetEmergencyAsync(EmergencyId);
        }
        catch (Exception)
        {
        }

        if (!IsCurrent(generation))
        {
            return;
        }

        if (emergency?.Patient is not null)
        {
            ErrorMessage = null;
            StatusMessage = ParamedicMessages.QrAlreadyIdentified;
            State = QrScannerState.Completed;
        }
        else
        {
            Pause(ParamedicMessages.QrConflict);
        }
    }

    private void RestartAfterInvalidCode()
    {
        Discard();
        ErrorMessage = null;
        StatusMessage = ParamedicMessages.QrInvalidOrExpired;
        StartScanning(delayed: true);
    }

    private void Pause(string message)
    {
        Discard();
        IsDetecting = false;
        ErrorMessage = message;
        State = QrScannerState.Paused;
    }

    // Cámara activa; la detección, al momento o tras una pausa (el código rechazado sigue delante de la cámara).
    private void StartScanning(bool delayed)
    {
        _rearm?.Cancel();
        IsDetecting = false;
        State = QrScannerState.Scanning;
        if (!delayed)
        {
            IsDetecting = true;
            return;
        }

        var rearm = _rearm = new CancellationTokenSource();
        _ = RearmAsync(_generation, rearm.Token);
    }

    private async Task RearmAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            await _delay(RearmDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!cancellationToken.IsCancellationRequested && generation == _generation && State == QrScannerState.Scanning)
        {
            IsDetecting = true;
        }
    }

    private void Discard()
    {
        _payload = null;
        _confirmRequestId = null;
        CandidateName = null;
        CandidateBirthDate = null;
        CandidateDocument = null;
    }

    private bool IsCurrent(int generation) =>
        generation == Volatile.Read(ref _generation) && _owner is { } owner && _session.CurrentUser?.Id == owner;

    // Otra cuenta (o ninguna): nada de la anterior queda en pantalla ni puede llegar tarde.
    private void OnSessionChanged(object? sender, EventArgs e)
    {
        var user = _session.CurrentUser?.Id;
        if (user == _owner)
        {
            return;
        }

        Interlocked.Increment(ref _generation);
        _visit?.Cancel();
        _rearm?.Cancel();
        if (_ui is null || SynchronizationContext.Current == _ui)
        {
            ResetForSession(user);
        }
        else
        {
            _ui.Post(_ => ResetForSession(_session.CurrentUser?.Id), null);
        }
    }

    private void ResetForSession(Guid? user)
    {
        Interlocked.Increment(ref _generation);
        _owner = user;
        _requestedPermission = false;
        Discard();
        IsDetecting = false;
        ErrorMessage = null;
        StatusMessage = null;
        CanOpenSettings = false;
        State = user is null ? QrScannerState.Unavailable : QrScannerState.Permission;
    }
}
