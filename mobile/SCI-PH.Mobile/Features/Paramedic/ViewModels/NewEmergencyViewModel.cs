using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Location;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

public partial class NewEmergencyViewModel : BaseViewModel
{
    private readonly IParamedicApiService _api;
    private readonly IAppNavigator _navigator;
    private readonly IDeviceLocationService _location;

    // Se conservan entre reintentos del mismo envío para que el servidor no cree dos emergencias;
    // se renuevan cuando la emergencia se crea correctamente.
    private Guid? _clientRequestId;
    private DateTime? _startedAt;

    public NewEmergencyViewModel(IParamedicApiService api, IAppNavigator navigator, IDeviceLocationService location)
    {
        _api = api;
        _navigator = navigator;
        _location = location;
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
            _clientRequestId ??= Guid.NewGuid();
            _startedAt ??= DateTime.UtcNow;

            var emergency = await _api.CreateEmergencyAsync(new CreateEmergencyDto
            {
                ClientRequestId = _clientRequestId.Value,
                StartedAt = _startedAt.Value,
                ChiefComplaint = ChiefComplaint.Trim(),
                Location = await TryGetLocationAsync()
            });

            _clientRequestId = null;
            _startedAt = null;
            ChiefComplaint = string.Empty;

            await _navigator.GoToAsync(
                ParamedicRoutes.IdentifyPatient,
                new Dictionary<string, object> { ["EmergencyId"] = emergency.Id });
        }, "No se pudo crear la emergencia. Intenta nuevamente.");
    }

    // Mejor esfuerzo: si no hay permiso, GPS o posición conocida, la emergencia se crea sin coordenadas.
    private async Task<LocationDto?> TryGetLocationAsync()
    {
        var location = await _location.TryGetLocationAsync();
        return location is null
            ? null
            : new LocationDto { Latitude = location.Latitude, Longitude = location.Longitude };
    }
}
