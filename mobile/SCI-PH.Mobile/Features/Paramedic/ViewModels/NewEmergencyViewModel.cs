using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

public partial class NewEmergencyViewModel : BaseViewModel
{
    private readonly IParamedicApiService _api;
    private readonly IAppNavigator _navigator;

    public NewEmergencyViewModel(IParamedicApiService api, IAppNavigator navigator)
    {
        _api = api;
        _navigator = navigator;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string chiefComplaint = string.Empty;

    protected override void OnBusyChanged() => SaveCommand.NotifyCanExecuteChanged();

    private bool CanSave() => !IsBusy && !string.IsNullOrWhiteSpace(ChiefComplaint);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        await RunAsync(async () =>
        {
            var emergency = await _api.CreateEmergencyAsync(new CreateEmergencyDto
            {
                ChiefComplaint = ChiefComplaint.Trim(),
                Location = await TryGetLocationAsync()
            });

            ChiefComplaint = string.Empty;

            await _navigator.GoToAsync(
                ParamedicRoutes.IdentifyPatient,
                new Dictionary<string, object> { ["EmergencyId"] = emergency.Id });
        }, "No se pudo crear la emergencia. Intenta nuevamente.");
    }

    // Mejor esfuerzo: si no hay permiso o GPS, la emergencia se crea sin coordenadas.
    // Para que funcione hay que declarar ACCESS_FINE_LOCATION en AndroidManifest.xml
    // y solicitar el permiso en tiempo de ejecución (pendiente).
    private static async Task<LocationDto?> TryGetLocationAsync()
    {
        try
        {
            var loc = await Geolocation.Default.GetLastKnownLocationAsync();
            return loc is null
                ? null
                : new LocationDto { Latitude = loc.Latitude, Longitude = loc.Longitude };
        }
        catch (Exception)
        {
            return null;
        }
    }
}
