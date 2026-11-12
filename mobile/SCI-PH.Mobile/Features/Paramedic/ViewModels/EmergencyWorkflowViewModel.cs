using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.Models;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

/// <summary>
/// Base de las pantallas del flujo de UNA emergencia: su id (parámetro de navigación "EmergencyId"), la identidad que muestra
/// la cabecera (código NN o paciente, siempre desde el servidor) y la vuelta al inicio del paramédico. No guarda una copia
/// propia del paciente: la cabecera se rehace con cada lectura de la emergencia.
/// </summary>
public abstract partial class EmergencyWorkflowViewModel : BaseViewModel
{
    private readonly IParamedicApiService _api;
    private readonly IAppNavigator _navigator;
    private int _identityGeneration;

    protected EmergencyWorkflowViewModel(IParamedicApiService api, IAppNavigator navigator)
    {
        _api = api;
        _navigator = navigator;
    }

    [ObservableProperty]
    private Guid emergencyId;

    /// <summary>Identidad de la emergencia en la cabecera; null hasta la primera lectura.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIdentity))]
    private EmergencyIdentity? identity;

    public bool HasIdentity => Identity is not null;

    /// <summary>
    /// Lee la emergencia y rehace la cabecera. Si falla, la cabecera queda como estaba (nunca bloquea el registro). Una
    /// respuesta de otra emergencia o de una lectura ya sustituida se descarta.
    /// </summary>
    public async Task RefreshIdentityAsync()
    {
        var id = EmergencyId;
        if (id == Guid.Empty)
        {
            return;
        }

        var generation = Interlocked.Increment(ref _identityGeneration);
        EmergencyDto? emergency;
        try
        {
            emergency = await _api.GetEmergencyAsync(id);
        }
        catch (Exception)
        {
            return;
        }

        if (emergency is not null && generation == Volatile.Read(ref _identityGeneration) && id == EmergencyId)
        {
            OnEmergencyRead(emergency);
        }
    }

    /// <summary>Una lectura de la emergencia que la pantalla ya hizo por su cuenta: se reutiliza para la cabecera.</summary>
    protected void ApplyEmergency(EmergencyDto emergency)
    {
        if (emergency.Id != EmergencyId)
        {
            return;
        }

        Interlocked.Increment(ref _identityGeneration);
        Identity = EmergencyIdentity.From(emergency);
    }

    /// <summary>Para que una pantalla reaccione al estado leído (por ejemplo, la identificación ya hecha).</summary>
    protected virtual void OnEmergencyRead(EmergencyDto emergency) => Identity = EmergencyIdentity.From(emergency);

    /// <summary>Vuelve al inicio del paramédico (reemplaza la pila); la emergencia sigue en "Emergencias activas".</summary>
    [RelayCommand]
    private Task GoHomeAsync() => _navigator.GoToAsync(ParamedicRoutes.HomeRoot);
}
