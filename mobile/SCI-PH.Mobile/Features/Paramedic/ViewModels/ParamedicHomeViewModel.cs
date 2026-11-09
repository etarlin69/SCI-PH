using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.Models;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

/// <summary>
/// Inicio del paramédico: "Nueva emergencia" y "Emergencias activas". La lista es la del servidor (las emergencias que el
/// paramédico actual aún puede continuar): no se guarda en el teléfono ni se filtra aquí. Retomar un caso vuelve a leer la
/// emergencia y entra en la pantalla que corresponde a su estado ACTUAL, nunca al de la lista.
/// Shell conserva esta página mientras vive y la app tiene una sola sesión, así que la lista pertenece a la cuenta que la
/// pidió: si la sesión pasa a otra cuenta o se cierra se borra de inmediato, y una respuesta tardía (de otra cuenta o de una
/// carga ya sustituida) se descarta.
/// </summary>
public sealed partial class ParamedicHomeViewModel : BaseViewModel
{
    private readonly IParamedicApiService _api;
    private readonly IAppNavigator _navigator;
    private readonly IClinicalSessionService _session;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private Guid? _owner;
    private int _loadGeneration;
    private int _sessionGeneration;

    public ParamedicHomeViewModel(IParamedicApiService api, IAppNavigator navigator, IClinicalSessionService session)
    {
        _api = api;
        _navigator = navigator;
        _session = session;
        _session.SessionChanged += OnSessionChanged;
        Emergencies.CollectionChanged += (_, _) => NotifyListState();
    }

    public ObservableCollection<ActiveEmergencyItem> Emergencies { get; } = new();

    /// <summary>Usuario de la sesión, para la cabecera.</summary>
    public string DisplayName => _session.CurrentUser?.UserName ?? string.Empty;

    /// <summary>La última carga terminó bien (para distinguir "sin emergencias" de "aún no se sabe").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool hasLoaded;

    /// <summary>Se está leyendo una emergencia para retomarla.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(NewEmergencyCommand))]
    private bool isOpening;

    public bool HasEmergencies => Emergencies.Count > 0;

    public bool IsEmpty => HasLoaded && !IsBusy && !HasError && Emergencies.Count == 0;

    protected override void OnBusyChanged() => NotifyListState();

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(ErrorMessage))
        {
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Carga (o recarga) la lista del servidor. Si falla, la lista queda vacía con el mensaje y "Reintentar".</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        var owner = _session.CurrentUser?.Id;
        var generation = Interlocked.Increment(ref _loadGeneration);
        _owner = owner;
        ErrorMessage = null;
        if (owner is null)
        {
            Clear();
            IsBusy = false;
            return;
        }

        IsBusy = true;
        try
        {
            var items = await _api.GetActiveEmergenciesAsync();
            if (!IsCurrentLoad(owner, generation))
            {
                return;
            }

            Emergencies.Clear();
            foreach (var item in items)
            {
                Emergencies.Add(ActiveEmergencyItem.From(item));
            }

            HasLoaded = true;
        }
        catch (Exception) when (!IsCurrentLoad(owner, generation))
        {
            // De otra cuenta o de una carga ya sustituida: no es un error de esta pantalla.
        }
        catch (Exception ex)
        {
            Clear();
            ErrorMessage = ex is MobileApiException api && api.Error is not (MobileApiError.Server or MobileApiError.InvalidResponse)
                ? MobileApiErrorMessages.For(api)
                : ParamedicMessages.ActiveEmergenciesFailed;
        }
        finally
        {
            if (IsCurrentLoad(owner, generation))
            {
                IsBusy = false;
            }
        }
    }

    private bool CanStart() => !IsOpening;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task NewEmergencyAsync() => _navigator.GoToAsync(ParamedicRoutes.NewEmergency);

    /// <summary>
    /// Retoma la emergencia: la lee del servidor y entra en la pantalla de su estado actual. Si ya no se puede continuar
    /// (entregada, cerrada, sin acceso), lo dice y recarga la lista.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task OpenAsync(ActiveEmergencyItem? item)
    {
        if (item is null || IsOpening)
        {
            return;
        }

        var session = Volatile.Read(ref _sessionGeneration);
        var owner = _session.CurrentUser?.Id;
        ErrorMessage = null;
        IsOpening = true;
        EmergencyDto? emergency = null;
        var unavailable = false;
        try
        {
            emergency = await _api.GetEmergencyAsync(item.Id);
            unavailable = emergency is null || emergency.Status == EmergencyStatus.Closed;
        }
        catch (Exception) when (!IsSameSession(session, owner))
        {
        }
        catch (MobileApiException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            unavailable = true;
        }
        catch (MobileApiException ex) when (ex.Error is not (MobileApiError.Server or MobileApiError.InvalidResponse))
        {
            ErrorMessage = MobileApiErrorMessages.For(ex);
        }
        catch (Exception)
        {
            ErrorMessage = ParamedicMessages.OpenEmergencyFailed;
        }
        finally
        {
            IsOpening = false;
        }

        // La sesión cambió mientras se leía: no se entra en una emergencia de otra cuenta.
        if (!IsSameSession(session, owner))
        {
            return;
        }

        if (unavailable)
        {
            await LoadAsync();
            ErrorMessage = ParamedicMessages.EmergencyNoLongerActive;
            return;
        }

        if (emergency is not null)
        {
            await _navigator.GoToAsync(ResumeRoute(emergency), new Dictionary<string, object> { ["EmergencyId"] = emergency.Id });
        }
    }

    /// <summary>
    /// Pantalla en la que se retoma una emergencia, según su estado en el servidor: con un traslado solicitado o en curso, el
    /// traslado (desde ahí se sigue documentando); sin identificar, la identificación; identificada, los signos vitales.
    /// </summary>
    public static string ResumeRoute(EmergencyDto emergency) =>
        emergency.ActiveTransfer?.Status is TransferStatus.Requested or TransferStatus.InTransit
            ? ParamedicRoutes.Transfer
            : emergency.Patient is null ? ParamedicRoutes.IdentifyPatient : ParamedicRoutes.VitalSigns;

    private bool IsCurrentLoad(Guid? owner, int generation) =>
        owner is not null && generation == Volatile.Read(ref _loadGeneration) && _session.CurrentUser?.Id == owner;

    private bool IsSameSession(int session, Guid? owner) =>
        session == Volatile.Read(ref _sessionGeneration) && owner is not null && _session.CurrentUser?.Id == owner;

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (_session.CurrentUser?.Id == _owner && _owner is not null)
        {
            // Renovar el token de la misma cuenta no borra nada.
            OnPropertyChanged(nameof(DisplayName));
            return;
        }

        _owner = null;
        Interlocked.Increment(ref _loadGeneration);
        Interlocked.Increment(ref _sessionGeneration);

        // La sesión puede cambiar fuera del hilo de la interfaz (por ejemplo, un refresh rechazado).
        if (_ui is null || SynchronizationContext.Current == _ui)
        {
            ResetForSession();
        }
        else
        {
            _ui.Post(_ => ResetForSession(), null);
        }
    }

    private void ResetForSession()
    {
        Clear();
        ErrorMessage = null;
        IsBusy = false;
        IsOpening = false;
        OnPropertyChanged(nameof(DisplayName));
    }

    private void Clear()
    {
        Emergencies.Clear();
        HasLoaded = false;
    }

    private void NotifyListState()
    {
        OnPropertyChanged(nameof(HasEmergencies));
        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>Una fila de "Emergencias activas", ya con los textos que muestra.</summary>
public sealed record ActiveEmergencyItem(
    Guid Id,
    EmergencyIdentity Identity,
    string StartedText,
    string StatusText,
    string? TransferText,
    string? ChiefComplaint)
{
    public bool HasTransfer => !string.IsNullOrWhiteSpace(TransferText);

    public bool HasChiefComplaint => !string.IsNullOrWhiteSpace(ChiefComplaint);

    public static ActiveEmergencyItem From(ActiveEmergencyDto dto) => new(
        dto.Id,
        EmergencyIdentity.From(dto.TemporaryPatientCode, dto.PatientDisplayName),
        StartedLabel(dto.StartedAt),
        StatusLabel(dto.Status),
        dto.ActiveTransferStatus switch
        {
            TransferStatus.Requested => "Traslado solicitado",
            TransferStatus.InTransit => "Traslado en curso",
            _ => null
        },
        string.IsNullOrWhiteSpace(dto.ChiefComplaint) ? null : dto.ChiefComplaint.Trim());

    /// <summary>Estados en los que el servidor lista una emergencia activa; otro valor se muestra genérico.</summary>
    public static string StatusLabel(string status) => status switch
    {
        "Created" => "Abierta",
        "InProgress" => "En atención",
        "Transferred" => "Rumbo al hospital",
        _ => "En curso"
    };

    private static string StartedLabel(DateTime startedAtUtc)
    {
        var local = DateTime.SpecifyKind(startedAtUtc, DateTimeKind.Utc).ToLocalTime();
        var format = local.Date == DateTime.Now.Date ? "HH:mm" : "dd/MM HH:mm";
        return $"Inicio {local.ToString(format, CultureInfo.CurrentCulture)}";
    }
}
