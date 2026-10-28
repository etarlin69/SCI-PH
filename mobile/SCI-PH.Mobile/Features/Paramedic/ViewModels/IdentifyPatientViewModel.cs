using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

[QueryProperty(nameof(EmergencyId), "EmergencyId")]
public partial class IdentifyPatientViewModel : BaseViewModel
{
    /// <summary>Cada cuánto se consulta la sesión de huella mientras espera al lector.</summary>
    public static readonly TimeSpan FingerprintPollInterval = TimeSpan.FromSeconds(1);

    // Sin conexión al consultar: se sigue intentando hasta este margen después de que la sesión venza.
    private static readonly TimeSpan OfflineGrace = TimeSpan.FromSeconds(10);

    private readonly IParamedicApiService _api;
    private readonly IFingerprintIdentificationService _fingerprints;
    private readonly IAppNavigator _navigator;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    // Clave de idempotencia de la identificación por documento (no la de la apertura). Se genera al entrar al formulario y
    // se conserva entre reintentos, errores de red, 404 y 409; solo se descarta cuando la identificación se completa.
    private Guid? _documentRequestId;

    // Claves de la sesión de huella: la de apertura vale para ESE lector hasta que la sesión termina; la de confirmación,
    // para ESA sesión. Así un reintento tras un fallo de red nunca abre otra sesión ni identifica dos veces.
    private Guid? _sessionRequestId;
    private Guid? _sessionReaderId;
    private Guid? _confirmRequestId;
    private FingerprintSessionDto? _session;
    private CancellationTokenSource? _polling;

    public IdentifyPatientViewModel(
        IParamedicApiService api,
        IFingerprintIdentificationService fingerprints,
        IAppNavigator navigator,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _api = api;
        _fingerprints = fingerprints;
        _navigator = navigator;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>
    /// Etiqueta visible → tipo del contrato. Solo documentos que demuestran identidad: "Otro" no se ofrece porque el
    /// servidor no identifica con él.
    /// </summary>
    public static IReadOnlyDictionary<string, PatientDocumentType> DocumentTypeByLabel { get; } =
        new Dictionary<string, PatientDocumentType>
        {
            ["Cédula"] = PatientDocumentType.NationalId,
            ["Pasaporte"] = PatientDocumentType.Passport
        };

    public IReadOnlyList<string> DocumentTypes { get; } = DocumentTypeByLabel.Keys.ToList();

    [ObservableProperty]
    private Guid emergencyId;

    // ---- Resultado ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanIdentify))]
    [NotifyPropertyChangedFor(nameof(ShowFingerprintPanel))]
    private bool isIdentified;

    [ObservableProperty]
    private string? patientDisplayName;

    /// <summary>
    /// Mientras no esté identificado se ofrecen las tres vías a la vez (huella, documento, NN): ninguna es requisito de
    /// otra, porque en una emergencia la huella puede no ser posible. Una lectura en curso no bloquea las otras dos.
    /// </summary>
    public bool CanIdentify => !IsIdentified;

    // ---- Huella ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFingerprintPanel))]
    private bool fingerprintRequested;

    [ObservableProperty]
    private IReadOnlyList<FingerprintReaderDto> readers = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartFingerprintReadingCommand))]
    private FingerprintReaderDto? selectedReader;

    /// <summary>Hay una sesión esperando al lector (Pending): se consulta su estado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseReader))]
    [NotifyPropertyChangedFor(nameof(HasActiveFingerprint))]
    [NotifyCanExecuteChangedFor(nameof(StartFingerprintReadingCommand))]
    private bool isWaitingForReader;

    /// <summary>El lector encontró un candidato: falta la confirmación del paramédico.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseReader))]
    [NotifyPropertyChangedFor(nameof(HasActiveFingerprint))]
    [NotifyCanExecuteChangedFor(nameof(StartFingerprintReadingCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmFingerprintCommand))]
    private bool hasCandidate;

    [ObservableProperty]
    private string? candidateName;

    [ObservableProperty]
    private string? candidateBirthDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFingerprintMessage))]
    private string? fingerprintMessage;

    public bool ShowFingerprintPanel => FingerprintRequested && !IsIdentified;

    public bool CanChooseReader => !IsWaitingForReader && !HasCandidate;

    public bool HasActiveFingerprint => IsWaitingForReader || HasCandidate;

    public bool HasFingerprintMessage => !string.IsNullOrWhiteSpace(FingerprintMessage);

    /// <summary>La consulta en curso, si la hay (para las pruebas: esperar a que termine).</summary>
    public Task? FingerprintPolling { get; private set; }

    // ---- Formulario de documento ----
    [ObservableProperty]
    private bool showDocumentForm;

    [ObservableProperty]
    private string selectedDocumentType = "Cédula";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitDocumentCommand))]
    private string documentNumber = string.Empty;

    protected override void OnBusyChanged()
    {
        IdentifyByFingerprintCommand.NotifyCanExecuteChanged();
        StartFingerprintReadingCommand.NotifyCanExecuteChanged();
        ConfirmFingerprintCommand.NotifyCanExecuteChanged();
        SubmitDocumentCommand.NotifyCanExecuteChanged();
        ContinueWithoutIdentifyingCommand.NotifyCanExecuteChanged();
    }

    // ---- Huella: lectores ----

    // REAL: lectores activos con el permiso de huella. Si hay uno solo, queda elegido.
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task IdentifyByFingerprintAsync()
    {
        ErrorMessage = null;
        FingerprintRequested = true;
        if (HasActiveFingerprint)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Readers = await _fingerprints.GetReadersAsync();
            SelectedReader = Readers.FirstOrDefault(r => r.Id == SelectedReader?.Id) ?? (Readers.Count == 1 ? Readers[0] : null);
            FingerprintMessage = Readers.Count == 0 ? ParamedicMessages.NoFingerprintReaders : null;
        }
        catch (MobileApiException ex) when (ex.Error is not (MobileApiError.Server or MobileApiError.InvalidResponse))
        {
            ErrorMessage = MobileApiErrorMessages.For(ex);
        }
        catch (Exception)
        {
            ErrorMessage = ParamedicMessages.FingerprintReadersFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Huella: sesión ----

    private bool CanStartFingerprintReading() => !IsBusy && SelectedReader is not null && !IsWaitingForReader && !HasCandidate;

    // REAL: abre la sesión con el lector elegido y consulta su estado hasta que el lector responda o venza.
    [RelayCommand(CanExecute = nameof(CanStartFingerprintReading))]
    private async Task StartFingerprintReadingAsync()
    {
        if (SelectedReader is not { } reader)
        {
            ErrorMessage = ParamedicMessages.FingerprintReaderRequired;
            return;
        }

        ErrorMessage = null;
        FingerprintMessage = null;
        IsBusy = true;
        try
        {
            if (_sessionReaderId != reader.Id)
            {
                _sessionRequestId = null;
            }

            _sessionRequestId ??= Guid.NewGuid();
            _sessionReaderId = reader.Id;
            var session = await _fingerprints.OpenSessionAsync(EmergencyId, _sessionRequestId.Value, reader.Id);
            _confirmRequestId = null;
            ApplySession(session);
        }
        catch (MobileApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            ErrorMessage = ParamedicMessages.FingerprintReaderNotFound;
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.Conflict)
        {
            ErrorMessage = ParamedicMessages.FingerprintOpenConflict;
        }
        catch (MobileApiException ex) when (ex.Error is not (MobileApiError.Server or MobileApiError.InvalidResponse))
        {
            ErrorMessage = MobileApiErrorMessages.For(ex);
        }
        catch (Exception)
        {
            ErrorMessage = ParamedicMessages.FingerprintFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConfirmFingerprint() => !IsBusy && HasCandidate;

    // REAL: el servidor identifica la MISMA emergencia con el paciente de la sesión; la app no envía ningún paciente.
    [RelayCommand(CanExecute = nameof(CanConfirmFingerprint))]
    private async Task ConfirmFingerprintAsync()
    {
        if (_session is not { } session)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            _confirmRequestId ??= Guid.NewGuid();
            var emergency = await _fingerprints.ConfirmAsync(EmergencyId, session.Id, _confirmRequestId.Value);
            EndFingerprintSession(message: null);
            ApplyIdentified(emergency.Patient?.DisplayName);
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.Conflict)
        {
            // No se genera otra clave en silencio: la sesión se vuelve a consultar para mostrar su estado real.
            ErrorMessage = ParamedicMessages.FingerprintConfirmConflict;
            await RefreshSessionAsync(session.Id);
        }
        catch (MobileApiException ex) when (ex.Error is not (MobileApiError.Server or MobileApiError.InvalidResponse))
        {
            ErrorMessage = MobileApiErrorMessages.For(ex);
        }
        catch (Exception)
        {
            ErrorMessage = ParamedicMessages.FingerprintFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Cancela la lectura en curso (en el servidor también) y deja elegir otro lector o seguir por documento o NN.
    [RelayCommand]
    private async Task CancelFingerprintAsync()
    {
        StopFingerprintPolling();
        if (_session is { } session && HasActiveFingerprint)
        {
            await CancelOnServerQuietlyAsync(session.Id);
        }

        EndFingerprintSession(ParamedicMessages.FingerprintCancelled);
    }

    /// <summary>La página dejó de verse: se detiene la consulta (nunca queda una consulta huérfana).</summary>
    public void StopFingerprintPolling()
    {
        _polling?.Cancel();
        _polling?.Dispose();
        _polling = null;
    }

    /// <summary>La página vuelve a verse con una lectura en espera: se reanuda la consulta.</summary>
    public void ResumeFingerprintPolling()
    {
        if (IsWaitingForReader && _session is { } session && _polling is null)
        {
            StartPolling(session.Id);
        }
    }

    private void StartPolling(Guid sessionId)
    {
        StopFingerprintPolling();
        _polling = new CancellationTokenSource();
        FingerprintPolling = PollAsync(sessionId, _polling.Token);
    }

    // Consulta la sesión cada segundo mientras está Pending. Sin conexión sigue intentando hasta poco después del
    // vencimiento; cualquier otro fallo termina la lectura con un mensaje (documento y NN siguen disponibles).
    private async Task PollAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _delay(FingerprintPollInterval, cancellationToken);
                var session = await _fingerprints.GetSessionAsync(EmergencyId, sessionId, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                ApplySession(session);
                if (session is { Status: FingerprintSessionStatus.Matched, HasCandidate: false })
                {
                    // Coincidencia sin candidato que confirmar (referencia revocada o lector que ya no es válido): se
                    // suelta la sesión para poder leer de nuevo sin esperar a que venza.
                    await CancelOnServerQuietlyAsync(session.Id);
                }

                if (session.Status != FingerprintSessionStatus.Pending)
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (MobileApiException ex) when (ex.IsConnectivityFailure)
            {
                if (_session is { } current && DateTimeOffset.UtcNow < current.ExpiresAt + OfflineGrace)
                {
                    FingerprintMessage = ParamedicMessages.FingerprintCheckingOffline;
                    continue;
                }

                EndFingerprintSession(MobileApiErrorMessages.For(ex));
                return;
            }
            catch (Exception)
            {
                EndFingerprintSession(ParamedicMessages.FingerprintFailed);
                return;
            }
        }
    }

    private async Task RefreshSessionAsync(Guid sessionId)
    {
        try
        {
            var session = await _fingerprints.GetSessionAsync(EmergencyId, sessionId);
            ApplySession(session);
            if (session is { Status: FingerprintSessionStatus.Matched, HasCandidate: false })
            {
                // Coincidencia que ya no se puede confirmar (la referencia se revocó o el lector ya no es válido): se suelta
                // en el servidor, como en la consulta, para poder leer de nuevo sin esperar a que venza.
                await CancelOnServerQuietlyAsync(session.Id);
            }
        }
        catch (Exception)
        {
            EndFingerprintSession(message: null);
        }
    }

    // El estado del servidor es la única fuente de verdad de la lectura.
    private void ApplySession(FingerprintSessionDto session)
    {
        _session = session;
        switch (session.Status)
        {
            case FingerprintSessionStatus.Pending:
                IsWaitingForReader = true;
                HasCandidate = false;
                FingerprintMessage = ParamedicMessages.FingerprintWaiting;
                if (_polling is null)
                {
                    StartPolling(session.Id);
                }

                break;
            case FingerprintSessionStatus.Matched when session.HasCandidate:
                StopFingerprintPolling();
                IsWaitingForReader = false;
                CandidateName = session.CandidateName;
                CandidateBirthDate = session.CandidateDateOfBirth?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                HasCandidate = true;
                FingerprintMessage = null;
                break;
            case FingerprintSessionStatus.Confirmed:
                var confirmedName = CandidateName;
                EndFingerprintSession(message: null);
                ApplyIdentified(confirmedName);
                break;
            default:
                // Matched sin candidato (la referencia se revocó o el lector ya no es válido) cuenta como sin coincidencia.
                EndFingerprintSession(session switch
                {
                    { Status: FingerprintSessionStatus.Failed, Failure: FingerprintFailure.LowQuality } => ParamedicMessages.FingerprintLowQuality,
                    { Status: FingerprintSessionStatus.Failed } => ParamedicMessages.FingerprintSensorError,
                    { Status: FingerprintSessionStatus.Expired } => ParamedicMessages.FingerprintExpired,
                    { Status: FingerprintSessionStatus.Cancelled } => ParamedicMessages.FingerprintCancelled,
                    _ => ParamedicMessages.FingerprintNoMatch
                });
                break;
        }
    }

    // La sesión terminó: la próxima lectura lleva claves nuevas.
    private void EndFingerprintSession(string? message)
    {
        StopFingerprintPolling();
        _session = null;
        _sessionRequestId = null;
        _sessionReaderId = null;
        _confirmRequestId = null;
        IsWaitingForReader = false;
        HasCandidate = false;
        CandidateName = null;
        CandidateBirthDate = null;
        FingerprintMessage = message;
    }

    // ---- Documento ----

    [RelayCommand]
    private void OpenDocumentForm()
    {
        ErrorMessage = null;
        _documentRequestId ??= Guid.NewGuid();
        ShowDocumentForm = true;
    }

    private bool CanSubmitDocument() =>
        !IsBusy && !string.IsNullOrWhiteSpace(DocumentNumber);

    // REAL: el servidor busca el documento y vincula al paciente la MISMA emergencia. El número va tal como se escribió
    // (la normalización es del servidor). Sin coincidencia, la emergencia sigue NN: se puede corregir, reintentar o
    // continuar sin identificar.
    [RelayCommand(CanExecute = nameof(CanSubmitDocument))]
    private async Task SubmitDocumentAsync()
    {
        if (!DocumentTypeByLabel.TryGetValue(SelectedDocumentType ?? string.Empty, out var documentType))
        {
            ErrorMessage = ParamedicMessages.DocumentTypeRequired;
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            _documentRequestId ??= Guid.NewGuid();
            var emergency = await _api.IdentifyByDocumentAsync(EmergencyId, new IdentifyByDocumentDto
            {
                ClientRequestId = _documentRequestId.Value,
                DocumentType = documentType,
                DocumentNumber = DocumentNumber
            });

            if (emergency is null)
            {
                ErrorMessage = ParamedicMessages.PatientNotFoundByDocument;
                return;
            }

            _documentRequestId = null;
            await CancelFingerprintQuietlyAsync();
            ApplyIdentified(emergency.Patient?.DisplayName);
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.Conflict)
        {
            // No se genera otra clave en silencio: podría identificar otra vez la emergencia.
            ErrorMessage = ParamedicMessages.IdentificationConflict;
        }
        catch (MobileApiException ex) when (ex.Error is not (MobileApiError.Server or MobileApiError.InvalidResponse))
        {
            ErrorMessage = MobileApiErrorMessages.For(ex);
        }
        catch (Exception)
        {
            ErrorMessage = ParamedicMessages.IdentifyByDocumentFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // La atención ya nace como paciente NN en el dominio: continuar sin identificar
    // no registra nada en el servidor; solo avanza al siguiente paso (y suelta el lector si había una lectura).
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ContinueWithoutIdentifyingAsync()
    {
        await CancelFingerprintQuietlyAsync();
        await GoToNextStepAsync();
    }

    [RelayCommand]
    private async Task ContinueAsync()
    {
        await GoToNextStepAsync();
    }

    // ---- Helpers ----
    private bool CanRun() => !IsBusy;

    // Otra vía terminó la identificación: la lectura pendiente se cancela sin mostrar nada (y si falla, vence sola).
    private async Task CancelFingerprintQuietlyAsync()
    {
        if (_session is { } session && HasActiveFingerprint)
        {
            StopFingerprintPolling();
            await CancelOnServerQuietlyAsync(session.Id);
        }

        EndFingerprintSession(message: null);
    }

    // Si falla, la sesión vence sola a los 90 s: nunca bloquea al paramédico.
    private async Task CancelOnServerQuietlyAsync(Guid sessionId)
    {
        try
        {
            await _fingerprints.CancelAsync(EmergencyId, sessionId);
        }
        catch (Exception)
        {
        }
    }

    private void ApplyIdentified(string? displayName)
    {
        PatientDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? "Paciente identificado"
            : displayName;
        IsIdentified = true;
        FingerprintRequested = false;
        ShowDocumentForm = false;
    }

    private Task GoToNextStepAsync() =>
        _navigator.GoToAsync(
            ParamedicRoutes.VitalSigns,
            new Dictionary<string, object> { ["EmergencyId"] = EmergencyId });
}
