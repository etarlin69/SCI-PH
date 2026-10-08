using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

public partial class NewEmergencyViewModel : ObservableObject
{
    private readonly IParamedicApiService _api;

    public NewEmergencyViewModel(IParamedicApiService api)
    {
        _api = api;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string chiefComplaint = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    private bool CanSave() => !IsBusy && !string.IsNullOrWhiteSpace(ChiefComplaint);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        IsBusy = true;

        try
        {
            var emergency = await _api.CreateEmergencyAsync(new CreateEmergencyDto
            {
                ChiefComplaint = ChiefComplaint.Trim(),
                Location = await TryGetLocationAsync()
            });

            // TODO: navegar a Identificación pasando emergency.Id
            await Shell.Current.DisplayAlert(
                "Emergencia creada",
                $"ID: {emergency.Id}\nEstado: {emergency.Status}",
                "OK");

            ChiefComplaint = string.Empty;
        }
        catch (Exception)
        {
            // Mensaje genérico: no exponer detalles internos al usuario.
            ErrorMessage = "No se pudo crear la emergencia. Intenta nuevamente.";
        }
        finally
        {
            IsBusy = false;
        }
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
