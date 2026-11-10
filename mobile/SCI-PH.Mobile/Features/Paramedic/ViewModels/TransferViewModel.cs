using System.Collections.ObjectModel;
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

/// <summary>
/// Traslado REAL de la emergencia: elegir una clínica activa, solicitar, iniciar el traslado (el paciente sigue a cargo del
/// paramédico, que puede seguir documentando) o cancelarlo. La recepción la hace el hospital: la app no la ofrece. El
/// estado se restaura desde el activeTransfer de la emergencia cada vez que se abre la pantalla.
/// </summary>
[QueryProperty(nameof(EmergencyId), "EmergencyId")]
public partial class TransferViewModel : EmergencyWorkflowViewModel
{
    /// <summary>Límites del servidor (ClinicalTransfer.Reason y Notes).</summary>
    public const int ReasonMaxLength = 500;
    public const int NotesMaxLength = 4000;

    private readonly IParamedicApiService _api;
    private readonly IParamedicCatalogService _catalog;
    private readonly IAppNavigator _navigator;

    // Una solicitud = una clave, generada en el primer intento y conservada en cada reintento (error de red, respuesta
    // perdida, 400/403/404/409) para no crear dos traslados. Se renueva cuando la solicitud queda registrada.
    private Guid? _clientRequestId;

    // Lecturas de la pantalla: solo la última, y solo de la emergencia que se está mostrando, cambia el estado.
    private int _loadGeneration;

    public TransferViewModel(IParamedicApiService api, IParamedicCatalogService catalog, IAppNavigator navigator)
        : base(api, navigator)
    {
        _api = api;
        _catalog = catalog;
        _navigator = navigator;
    }


    public ObservableCollection<TransferDestinationDto> Destinations { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestCommand))]
    private TransferDestinationDto? selectedDestination;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestCommand))]
    private string reason = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestCommand))]
    private string notes = string.Empty;

    /// <summary>El traslado solicitado o en curso; null mientras no hay ninguno.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoTransfer), nameof(ShowRequestForm), nameof(HasTransfer), nameof(IsRequested), nameof(IsInTransit), nameof(StatusText), nameof(DestinationText))]
    [NotifyCanExecuteChangedFor(nameof(RequestCommand), nameof(StartCommand), nameof(CancelCommand))]
    private TransferDto? transfer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuccess))]
    private string? successMessage;

    public bool HasSuccess => !string.IsNullOrWhiteSpace(SuccessMessage);

    /// <summary>
    /// "Identificar paciente": el paciente sigue NN y la emergencia todavía es del paramédico (sin recepción del hospital),
    /// según la última lectura del servidor. Identificar no toca el traslado; tras la recepción el servidor ya no lo acepta.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(IdentifyPatientCommand))]
    private bool canIdentifyPatient;

    public bool HasNoTransfer => Transfer is null;

    /// <summary>
    /// El hospital ya recibió la emergencia (última lectura del servidor): no se ofrece ninguna acción de traslado, solo el
    /// aviso. Es una guía de la pantalla; el servidor sigue rechazando (409) cualquier paso tras la recepción.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRequestForm), nameof(ShowInstructions))]
    [NotifyCanExecuteChangedFor(nameof(RequestCommand), nameof(StartCommand), nameof(CancelCommand))]
    private bool isHandedOff;

    /// <summary>Las instrucciones del traslado, que dejan de aplicar tras la recepción.</summary>
    public bool ShowInstructions => !IsHandedOff;

    public string HandedOffMessage => ParamedicMessages.TransferHandedOff;

    /// <summary>Formulario de solicitud (destino, motivo, notas): sin traslado activo y antes de la recepción.</summary>
    public bool ShowRequestForm => Transfer is null && !IsHandedOff;
    public bool HasTransfer => Transfer is not null;
    public bool IsRequested => Transfer?.Status == TransferStatus.Requested;
    public bool IsInTransit => Transfer?.Status == TransferStatus.InTransit;

    public string StatusText => Transfer?.Status switch
    {
        TransferStatus.Requested => $"Traslado solicitado a las {Time(Transfer.RequestedAt)}",
        TransferStatus.InTransit => $"En traslado desde las {Time(Transfer.SentAt ?? Transfer.RequestedAt)}",
        _ => string.Empty
    };

    public string DestinationText => Transfer is null
        ? string.Empty
        : IsInTransit ? $"Paciente en traslado hacia {Transfer.Destination.Name}" : $"Destino: {Transfer.Destination.DisplayName}";

    protected override void OnBusyChanged()
    {
        RequestCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        IdentifyPatientCommand.NotifyCanExecuteChanged();
    }

    // Restaura el estado desde el servidor (activeTransfer) y carga los destinos si todavía no hay traslado.
    [RelayCommand]
    private async Task LoadAsync()
    {
        var id = EmergencyId;
        var generation = Interlocked.Increment(ref _loadGeneration);
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var emergency = await _api.GetEmergencyAsync(id);
            if (!IsCurrentLoad(id, generation))
            {
                return;
            }

            if (emergency is not null)
            {
                // La misma lectura sirve para la cabecera (código NN o paciente).
                ApplyEmergency(emergency);
            }

            CanIdentifyPatient = emergency is { Patient: null, Status: EmergencyStatus.Open or EmergencyStatus.Transferred };
            IsHandedOff = emergency?.Status == EmergencyStatus.HandedOff;
            Transfer = IsHandedOff ? null : emergency?.ActiveTransfer;
            if (Transfer is null && !IsHandedOff && Destinations.Count == 0)
            {
                var destinations = await _catalog.GetTransferDestinationsAsync();
                if (IsCurrentLoad(id, generation) && Destinations.Count == 0)
                {
                    foreach (var destination in destinations) Destinations.Add(destination);
                }
            }
        }
        catch (Exception ex)
        {
            if (IsCurrentLoad(id, generation))
            {
                CanIdentifyPatient = false;
                ErrorMessage = ex is MobileApiException api && api.Error is not (MobileApiError.Server or MobileApiError.InvalidResponse)
                    ? MobileApiErrorMessages.For(api)
                    : ParamedicMessages.TransferDestinationsFailed;
            }
        }
        finally
        {
            if (generation == Volatile.Read(ref _loadGeneration))
            {
                IsBusy = false;
            }
        }
    }

    private bool CanRequest() =>
        !IsBusy && !IsHandedOff && Transfer is null && SelectedDestination is not null
        && (Reason?.Trim().Length ?? 0) <= ReasonMaxLength && (Notes?.Trim().Length ?? 0) <= NotesMaxLength;

    [RelayCommand(CanExecute = nameof(CanRequest))]
    private Task RequestAsync() => RunTransferAsync(async () =>
    {
        _clientRequestId ??= Guid.NewGuid();
        var result = await _api.RequestTransferAsync(EmergencyId, new RequestTransferDto
        {
            ClientRequestId = _clientRequestId.Value,
            DestinationClinicId = SelectedDestination!.Id,
            Reason = string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim(),
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim()
        });
        _clientRequestId = null;
        Reason = Notes = string.Empty;
        return result;
    }, ParamedicMessages.RequestTransferFailed);

    private bool CanStart() => !IsBusy && !IsHandedOff && IsRequested;

    // Sobre ESE traslado; repetirlo tras una respuesta perdida devuelve el mismo traslado en curso.
    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartAsync() =>
        RunTransferAsync(() => _api.StartTransferAsync(EmergencyId, Transfer!.Id), ParamedicMessages.TransferStepFailed);

    private bool CanCancel() => !IsBusy && !IsHandedOff && (IsRequested || IsInTransit);

    // Sobre ESE traslado; tras cancelarlo se puede solicitar otro destino.
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync()
    {
        var cancelled = await RunTransferAsync(() => _api.CancelTransferAsync(EmergencyId, Transfer!.Id), ParamedicMessages.TransferStepFailed);
        if (cancelled?.Status == TransferStatus.Cancelled)
        {
            Transfer = null;
            SuccessMessage = ParamedicMessages.TransferCancelled;
            if (Destinations.Count == 0) await LoadAsync();
        }
    }

    private bool IsCurrentLoad(Guid id, int generation) =>
        generation == Volatile.Read(ref _loadGeneration) && id == EmergencyId;

    private bool CanOpenIdentification() => !IsBusy && CanIdentifyPatient;

    // La identificación de SIEMPRE (huella, documento, QR) sobre la misma emergencia; al terminar vuelve aquí. El traslado
    // no se toca: sigue solicitado o en curso.
    [RelayCommand(CanExecute = nameof(CanOpenIdentification))]
    private Task IdentifyPatientAsync() =>
        _navigator.GoToAsync(ParamedicRoutes.IdentifyPatient, new Dictionary<string, object>
        {
            ["EmergencyId"] = EmergencyId,
            [ParamedicRoutes.ReturnToTransfer] = true
        });

    // Durante el traslado se sigue documentando sobre la misma emergencia.
    [RelayCommand]
    private Task GoToVitalSignsAsync() => GoAsync(ParamedicRoutes.VitalSigns);

    [RelayCommand]
    private Task GoToProceduresAsync() => GoAsync(ParamedicRoutes.Procedures);

    [RelayCommand]
    private Task GoToMedicationsAsync() => GoAsync(ParamedicRoutes.Medications);

    [RelayCommand]
    private Task GoToNotesAsync() => GoAsync(ParamedicRoutes.Notes);

    private Task GoAsync(string route) =>
        _navigator.GoToAsync(route, new Dictionary<string, object> { ["EmergencyId"] = EmergencyId });

    // Runs a transfer step with safe messages; on success the returned transfer becomes the screen's state.
    private async Task<TransferDto?> RunTransferAsync(Func<Task<TransferDto>> step, string failure)
    {
        ErrorMessage = null;
        SuccessMessage = null;
        IsBusy = true;
        try
        {
            var result = await step();
            Transfer = result.Status is TransferStatus.Requested or TransferStatus.InTransit ? result : null;
            return result;
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.Conflict)
        {
            ErrorMessage = ParamedicMessages.TransferConflict;
        }
        catch (MobileApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            ErrorMessage = ParamedicMessages.TransferNotFound;
        }
        catch (MobileApiException ex) when (ex.Error is not (MobileApiError.Server or MobileApiError.InvalidResponse))
        {
            ErrorMessage = MobileApiErrorMessages.For(ex);
        }
        catch (Exception)
        {
            ErrorMessage = failure;
        }
        finally
        {
            IsBusy = false;
        }

        return null;
    }

    private static string Time(DateTime utc) => utc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
}
