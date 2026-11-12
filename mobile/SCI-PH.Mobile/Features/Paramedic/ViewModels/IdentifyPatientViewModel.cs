using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

/// <summary>Pasos de la identificación por documento: nada se identifica sin "Confirmar paciente".</summary>
public enum DocumentIdentificationStep
{
    /// <summary>Tipo y número editables; "Buscar paciente".</summary>
    Entry,
    Resolving,

    /// <summary>Candidato en pantalla (nombre, nacimiento, documento enmascarado): confirmar o buscar otro.</summary>
    Candidate,
    Confirming
}

[QueryProperty(nameof(EmergencyId), "EmergencyId")]
[QueryProperty(nameof(ReturnsToTransfer), ParamedicRoutes.ReturnToTransfer)]
public partial class IdentifyPatientViewModel : EmergencyWorkflowViewModel
{
    /// <summary>Cada cuánto se consulta la sesión de huella mientras espera al lector.</summary>
    public static readonly TimeSpan FingerprintPollInterval = TimeSpan.FromSeconds(1);

    // Sin conexión al consultar: se sigue intentando hasta este margen después de que la sesión venza.
    private static readonly TimeSpan OfflineGrace = TimeSpan.FromSeconds(10);

    private readonly IParamedicApiService _api;
    private readonly IFingerprintIdentificationService _fingerprints;
    private readonly IAppNavigator _navigator;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly IClinicalSessionService? _clinicalSession;

    // Se abrió el escáner de QR: al volver, la emergencia se lee del servidor (la única fuente de verdad del resultado).
    private bool _returningFromQrScanner;

    // Documento del candidato tal como se buscó (solo en memoria, nunca registrado): la confirmación envía ESE documento,
    // no lo que haya en el formulario, y el servidor lo vuelve a resolver. Sin id de paciente.
    private (PatientDocumentType Type, string Number)? _documentCandidate;

    // Clave de idempotencia de la CONFIRMACIÓN por documento (no la de la apertura): una por candidato, generada al confirmar
    // por primera vez y conservada en los reintentos (sin respuesta, 503, 429), así repetir nunca identifica dos veces. Se
    // descarta con el candidato (buscar otro, 404, 409) o cuando la identificación se completa.
    private Guid? _documentRequestId;

    // Las respuestas de una búsqueda o confirmación ya sustituida (otra búsqueda, buscar otro, salir de la página) o de otra
    // sesión se descartan.
    private int _documentGeneration;

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
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ICameraPermissionService? camera = null,
        IClinicalSessionService? clinicalSession = null)
        : base(api, navigator)
    {
        _clinicalSession = clinicalSession;
        _api = api;
        _fingerprints = fingerprints;
        _navigator = navigator;
        _delay = delay ?? Task.Delay;
        IsQrScannerSupported = camera?.IsScannerSupported ?? false;
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


    /// <summary>
    /// Abierta desde el traslado (paciente NN con el traslado ya solicitado o en curso): al terminar, con o sin
    /// identificación, se vuelve al traslado en vez de avanzar a los signos vitales.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContinueText), nameof(ContinueWithoutIdentifyingText))]
    private bool returnsToTransfer;

    public string ContinueText => ReturnsToTransfer ? "Volver al traslado" : "Continuar a signos vitales";

    public string ContinueWithoutIdentifyingText => ReturnsToTransfer ? "Volver al traslado sin identificar" : "Continuar sin identificar";

    // ---- Resultado ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanIdentify))]
    [NotifyPropertyChangedFor(nameof(ShowFingerprintPanel))]
    [NotifyPropertyChangedFor(nameof(ShowQrOption))]
    [NotifyPropertyChangedFor(nameof(ShowIdentifiedResult))]
    private bool isIdentified;

    [ObservableProperty]
    private string? patientDisplayName;

    /// <summary>
    /// El hospital ya recibió el caso (leído del servidor o deducido de un 409): la atención prehospitalaria terminó, así que
    /// no se ofrece ninguna vía ni "continuar"; solo volver al inicio. El servidor ya rechaza cualquier identificación.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanIdentify), nameof(ShowQrOption), nameof(ShowFingerprintPanel), nameof(ShowDocumentPanel),
        nameof(ShowTopError), nameof(ShowDocumentError), nameof(ShowIdentifiedResult))]
    private bool isHandedOff;

    /// <summary>
    /// Mientras no esté identificado se ofrecen las tres vías a la vez (huella, documento, NN): ninguna es requisito de
    /// otra, porque en una emergencia la huella puede no ser posible. Una lectura en curso no bloquea las otras dos.
    /// </summary>
    public bool CanIdentify => !IsIdentified && !IsHandedOff;

    /// <summary>La plataforma ofrece el escáner de QR (no en Mac Catalyst).</summary>
    public bool IsQrScannerSupported { get; }

    /// <summary>"Código QR del paciente": otra vía más, solo mientras la emergencia se puede identificar.</summary>
    public bool ShowQrOption => IsQrScannerSupported && CanIdentify;

    /// <summary>El formulario de documento abierto, mientras se puede identificar.</summary>
    public bool ShowDocumentPanel => ShowDocumentForm && !IsHandedOff;

    /// <summary>
    /// El error se muestra junto al formulario activo: con el documento abierto, dentro de su tarjeta (abajo, donde se está
    /// escribiendo); si no, arriba.
    /// </summary>
    public bool ShowDocumentError => HasError && ShowDocumentPanel;

    public bool ShowTopError => HasError && !ShowDocumentPanel;

    /// <summary>"Paciente identificado" con su "continuar", salvo si el hospital ya recibió el caso.</summary>
    public bool ShowIdentifiedResult => IsIdentified && !IsHandedOff;

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

    public bool ShowFingerprintPanel => FingerprintRequested && CanIdentify;

    public bool CanChooseReader => !IsWaitingForReader && !HasCandidate;

    public bool HasActiveFingerprint => IsWaitingForReader || HasCandidate;

    public bool HasFingerprintMessage => !string.IsNullOrWhiteSpace(FingerprintMessage);

    /// <summary>La consulta en curso, si la hay (para las pruebas: esperar a que termine).</summary>
    public Task? FingerprintPolling { get; private set; }

    // ---- Formulario de documento ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDocumentPanel), nameof(ShowTopError), nameof(ShowDocumentError))]
    private bool showDocumentForm;

    [ObservableProperty]
    private string selectedDocumentType = "Cédula";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResolveDocumentCommand))]
    private string documentNumber = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDocumentEntry), nameof(HasDocumentCandidate), nameof(IsDocumentWorking))]
    [NotifyCanExecuteChangedFor(nameof(ResolveDocumentCommand), nameof(ConfirmDocumentCommand), nameof(CancelDocumentCandidateCommand))]
    private DocumentIdentificationStep documentStep = DocumentIdentificationStep.Entry;

    [ObservableProperty]
    private string? documentCandidateName;

    [ObservableProperty]
    private string? documentCandidateBirthDate;

    [ObservableProperty]
    private string? documentCandidateDocument;

    /// <summary>El tipo y el número se pueden cambiar (no mientras se busca, hay candidato o se confirma).</summary>
    public bool IsDocumentEntry => DocumentStep == DocumentIdentificationStep.Entry;

    public bool HasDocumentCandidate => DocumentStep is DocumentIdentificationStep.Candidate or DocumentIdentificationStep.Confirming;

    public bool IsDocumentWorking => DocumentStep is DocumentIdentificationStep.Resolving or DocumentIdentificationStep.Confirming;

    protected override void OnErrorChanged()
    {
        OnPropertyChanged(nameof(ShowTopError));
        OnPropertyChanged(nameof(ShowDocumentError));
    }

    /// <summary>Cada lectura de la emergencia (al abrir la página o tras un 409) dice si el hospital ya la recibió.</summary>
    protected override void OnEmergencyRead(EmergencyDto emergency)
    {
        base.OnEmergencyRead(emergency);
        ApplyHandedOff(emergency);
    }

    // Recibida por el hospital: no queda nada que hacer aquí. Se suelta la lectura de huella y se olvida el candidato.
    private bool ApplyHandedOff(EmergencyDto? emergency)
    {
        if (emergency?.Status != EmergencyStatus.HandedOff)
        {
            return false;
        }

        if (_session is { } session)
        {
            _ = CancelOnServerQuietlyAsync(session.Id);
        }

        EndFingerprintSession(message: null);
        DiscardDocumentCandidate();
        ErrorMessage = null;
        IsHandedOff = true;
        return true;
    }

    protected override void OnBusyChanged()
    {
        IdentifyByFingerprintCommand.NotifyCanExecuteChanged();
        StartFingerprintReadingCommand.NotifyCanExecuteChanged();
        ConfirmFingerprintCommand.NotifyCanExecuteChanged();
        ResolveDocumentCommand.NotifyCanExecuteChanged();
        ConfirmDocumentCommand.NotifyCanExecuteChanged();
        CancelDocumentCandidateCommand.NotifyCanExecuteChanged();
        ContinueWithoutIdentifyingCommand.NotifyCanExecuteChanged();
        ScanQrCommand.NotifyCanExecuteChanged();
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
            // Puede ser que el hospital ya recibió el caso: la lectura de la emergencia lo dice.
            await RefreshIdentityAsync();
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
            ApplyEmergency(emergency);
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.Conflict)
        {
            // No se genera otra clave en silencio: la sesión se vuelve a consultar para mostrar su estado real.
            ErrorMessage = ParamedicMessages.FingerprintConfirmConflict;
            await RefreshSessionAsync(session.Id);
            await RefreshIdentityAsync();
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
                // La sesión no trae la emergencia: la cabecera se rehace con una lectura del servidor.
                _ = RefreshIdentityAsync();
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

    // ---- QR del paciente ----

    private bool CanScanQr() => !IsBusy && ShowQrOption;

    // El escaneo, el candidato y la confirmación ocurren en su propia página; la emergencia es la misma.
    [RelayCommand(CanExecute = nameof(CanScanQr))]
    private async Task ScanQrAsync()
    {
        if (!ShowQrOption)
        {
            return;
        }

        ErrorMessage = null;
        _returningFromQrScanner = true;
        await _navigator.GoToAsync(
            ParamedicRoutes.ScanQr,
            new Dictionary<string, object> { ["EmergencyId"] = EmergencyId });
    }

    /// <summary>
    /// La página vuelve a verse tras el escáner: si el servidor ya vincula un paciente a la emergencia, queda identificada (con
    /// el mismo código NN y todo lo registrado) y la lectura de huella pendiente se suelta. Sin paciente, nada cambia.
    /// </summary>
    public async Task RefreshAfterQrScannerAsync()
    {
        if (!_returningFromQrScanner || IsIdentified)
        {
            return;
        }

        _returningFromQrScanner = false;
        EmergencyDto? emergency;
        try
        {
            emergency = await _api.GetEmergencyAsync(EmergencyId);
        }
        catch (Exception)
        {
            // Sin respuesta: sigue como estaba; si ya se identificó, otro intento lo mostrará (409 y relectura).
            return;
        }

        if (emergency?.Patient is { } patient)
        {
            await CancelFingerprintQuietlyAsync();
            ApplyIdentified(patient.DisplayName);
            ApplyEmergency(emergency);
        }
    }

    // ---- Documento: buscar → candidato → confirmar ----

    [RelayCommand]
    private void OpenDocumentForm()
    {
        ErrorMessage = null;
        ShowDocumentForm = true;
    }

    private bool CanResolveDocument() =>
        !IsBusy && DocumentStep == DocumentIdentificationStep.Entry && !string.IsNullOrWhiteSpace(DocumentNumber);

    // REAL: el servidor busca el documento y devuelve un candidato (nombre, nacimiento, documento enmascarado). La emergencia
    // NO se identifica: solo "Confirmar paciente" lo hace. El número va tal como se escribió (la normalización es del
    // servidor). Sin coincidencia, la emergencia sigue NN: se puede corregir, reintentar o continuar sin identificar.
    [RelayCommand(CanExecute = nameof(CanResolveDocument))]
    private async Task ResolveDocumentAsync()
    {
        if (!DocumentTypeByLabel.TryGetValue(SelectedDocumentType ?? string.Empty, out var documentType))
        {
            ErrorMessage = ParamedicMessages.DocumentTypeRequired;
            return;
        }

        var number = DocumentNumber;
        var generation = Interlocked.Increment(ref _documentGeneration);
        var owner = SessionOwner();
        ErrorMessage = null;
        DocumentStep = DocumentIdentificationStep.Resolving;
        IsBusy = true;
        try
        {
            var candidate = await _api.ResolveDocumentCandidateAsync(EmergencyId, documentType, number);
            if (!IsCurrentDocument(generation, owner))
            {
                return;
            }

            _documentCandidate = (documentType, number);
            _documentRequestId = null;
            DocumentCandidateName = candidate.DisplayName;
            DocumentCandidateBirthDate = candidate.DateOfBirth?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                ?? ParamedicMessages.CandidateBirthDateUnknown;
            DocumentCandidateDocument = $"{DocumentLabel(candidate.DocumentType ?? documentType.ToString())} {candidate.DocumentValue}";
            DocumentStep = DocumentIdentificationStep.Candidate;
        }
        catch (Exception) when (!IsCurrentDocument(generation, owner))
        {
            // Respuesta de una búsqueda ya sustituida o de otra sesión: no es de esta pantalla.
        }
        catch (MobileApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            DocumentStep = DocumentIdentificationStep.Entry;
            ErrorMessage = ParamedicMessages.PatientNotFoundByDocument;
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.Conflict)
        {
            await ResolveDocumentConflictAsync(generation, owner);
        }
        catch (MobileApiException ex) when (ex.Error is not (MobileApiError.Server or MobileApiError.InvalidResponse))
        {
            DocumentStep = DocumentIdentificationStep.Entry;
            ErrorMessage = MobileApiErrorMessages.For(ex);
        }
        catch (Exception)
        {
            DocumentStep = DocumentIdentificationStep.Entry;
            ErrorMessage = ParamedicMessages.DocumentResolveFailed;
        }
        finally
        {
            if (generation == Volatile.Read(ref _documentGeneration))
            {
                IsBusy = false;
            }
        }
    }

    private bool CanConfirmDocument() => !IsBusy && DocumentStep == DocumentIdentificationStep.Candidate;

    // REAL: identifica la MISMA emergencia. Nunca se llama sola tras buscar: solo con "Confirmar paciente". El servidor vuelve
    // a resolver el documento; la clave es la de ESTE candidato y se conserva si la respuesta se pierde.
    [RelayCommand(CanExecute = nameof(CanConfirmDocument))]
    private async Task ConfirmDocumentAsync()
    {
        if (DocumentStep != DocumentIdentificationStep.Candidate || _documentCandidate is not { } candidate)
        {
            return;
        }

        var generation = Volatile.Read(ref _documentGeneration);
        var owner = SessionOwner();
        _documentRequestId ??= Guid.NewGuid();
        var clientRequestId = _documentRequestId.Value;
        ErrorMessage = null;
        DocumentStep = DocumentIdentificationStep.Confirming;
        IsBusy = true;
        try
        {
            var emergency = await _api.IdentifyByDocumentAsync(EmergencyId, new IdentifyByDocumentDto
            {
                ClientRequestId = clientRequestId,
                DocumentType = candidate.Type,
                DocumentNumber = candidate.Number
            });
            if (!IsCurrentDocument(generation, owner))
            {
                return;
            }

            if (emergency is null)
            {
                // El documento ya no corresponde a un paciente: nunca se confía en el candidato anterior.
                DiscardDocumentCandidate();
                ErrorMessage = ParamedicMessages.DocumentCandidateGone;
                return;
            }

            await CancelFingerprintQuietlyAsync();
            ApplyIdentified(emergency.Patient?.DisplayName);
            ApplyEmergency(emergency);
        }
        catch (Exception) when (!IsCurrentDocument(generation, owner))
        {
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.Conflict)
        {
            await ResolveDocumentConflictAsync(generation, owner);
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.TooManyRequests)
        {
            // Rechazada antes de procesarse: el mismo candidato y la misma clave, reintento manual.
            DocumentStep = DocumentIdentificationStep.Candidate;
            ErrorMessage = MobileApiErrorMessages.For(ex);
        }
        catch (MobileApiException ex) when (ex.Error is MobileApiError.Forbidden or MobileApiError.Unauthorized)
        {
            DiscardDocumentCandidate();
            ErrorMessage = MobileApiErrorMessages.For(ex);
        }
        catch (Exception)
        {
            // Sin respuesta, 503 o respuesta inesperada: pudo aplicarse. El reintento lleva la MISMA clave.
            DocumentStep = DocumentIdentificationStep.Candidate;
            ErrorMessage = ParamedicMessages.DocumentConfirmFailed;
        }
        finally
        {
            if (generation == Volatile.Read(ref _documentGeneration))
            {
                IsBusy = false;
            }
        }
    }

    private bool CanCancelDocumentCandidate() => DocumentStep == DocumentIdentificationStep.Candidate;

    // "Buscar otro": el candidato se olvida y no se escribe nada; el formulario queda para corregir el documento.
    [RelayCommand(CanExecute = nameof(CanCancelDocumentCandidate))]
    private void CancelDocumentCandidate()
    {
        DiscardDocumentCandidate();
        ErrorMessage = null;
    }

    /// <summary>
    /// La página dejó de verse: una búsqueda en curso se descarta. Una confirmación ya enviada pudo aplicarse: su resultado lo
    /// da el servidor al volver (la cabecera relee la emergencia) y, si no se aplicó, el candidato sigue con la MISMA clave.
    /// </summary>
    public void DeactivateDocument()
    {
        Interlocked.Increment(ref _documentGeneration);
        switch (DocumentStep)
        {
            case DocumentIdentificationStep.Resolving:
                DocumentStep = DocumentIdentificationStep.Entry;
                IsBusy = false;
                break;
            case DocumentIdentificationStep.Confirming:
                DocumentStep = DocumentIdentificationStep.Candidate;
                ErrorMessage = ParamedicMessages.DocumentConfirmFailed;
                IsBusy = false;
                break;
        }
    }

    // 409 (ya identificada, recibida por el hospital o la emergencia cambió): se vuelve a leer. Con paciente, nunca se
    // sustituye: se muestra el que tiene; sin él, un conflicto genérico y el documento se puede buscar de nuevo.
    private async Task ResolveDocumentConflictAsync(int generation, Guid? owner)
    {
        DiscardDocumentCandidate();
        EmergencyDto? emergency = null;
        try
        {
            emergency = await _api.GetEmergencyAsync(EmergencyId);
        }
        catch (Exception)
        {
        }

        if (!IsCurrentDocument(generation, owner))
        {
            return;
        }

        if (emergency is not null && ApplyHandedOff(emergency))
        {
            ApplyEmergency(emergency);
            return;
        }

        if (emergency?.Patient is { } patient)
        {
            await CancelFingerprintQuietlyAsync();
            ApplyIdentified(patient.DisplayName);
            ApplyEmergency(emergency);
            ErrorMessage = ParamedicMessages.AlreadyIdentifiedByAnother;
        }
        else
        {
            ErrorMessage = ParamedicMessages.IdentificationConflict;
        }
    }

    private void DiscardDocumentCandidate()
    {
        _documentCandidate = null;
        _documentRequestId = null;
        DocumentCandidateName = null;
        DocumentCandidateBirthDate = null;
        DocumentCandidateDocument = null;
        DocumentStep = DocumentIdentificationStep.Entry;
    }

    private Guid? SessionOwner() => _clinicalSession?.CurrentUser?.Id;

    private bool IsCurrentDocument(int generation, Guid? owner) =>
        generation == Volatile.Read(ref _documentGeneration)
        && (_clinicalSession is null || (owner is not null && _clinicalSession.CurrentUser?.Id == owner));

    private static string DocumentLabel(string type) => type switch
    {
        "NationalId" => "Cédula",
        "Passport" => "Pasaporte",
        _ => "Documento"
    };

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
        DiscardDocumentCandidate();
    }

    // Desde el traslado se vuelve a la MISMA pantalla de traslado (que relee la emergencia); si no, el siguiente paso.
    private Task GoToNextStepAsync() =>
        ReturnsToTransfer
            ? _navigator.GoToAsync(ParamedicRoutes.Back)
            : _navigator.GoToAsync(
                ParamedicRoutes.VitalSigns,
                new Dictionary<string, object> { ["EmergencyId"] = EmergencyId });
}
