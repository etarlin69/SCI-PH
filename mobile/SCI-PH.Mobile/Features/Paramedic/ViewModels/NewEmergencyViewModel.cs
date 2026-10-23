using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

public partial class NewEmergencyViewModel : BaseViewModel
{
    private readonly IParamedicApiService _api;
    private readonly IAppNavigator _navigator;

    // Se conservan entre reintentos del mismo envío para que el servidor no cree dos emergencias (devuelve la misma);
    // se renuevan solo cuando la emergencia se crea correctamente. Nunca se regeneran por un error.
    private Guid? _clientRequestId;
    private DateTime? _startedAt;

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
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            _clientRequestId ??= Guid.NewGuid();
            _startedAt ??= DateTime.UtcNow;

            // 201 (nueva) o 200 (el servidor ya la había creado con esta clave): en ambos casos, su id real.
            var emergency = await _api.CreateEmergencyAsync(new CreateEmergencyDto
            {
                ClientRequestId = _clientRequestId.Value,
                StartedAt = _startedAt.Value,
                ChiefComplaint = ChiefComplaint.Trim()
            });

            _clientRequestId = null;
            _startedAt = null;
            ChiefComplaint = string.Empty;

            await _navigator.GoToAsync(
                ParamedicRoutes.IdentifyPatient,
                new Dictionary<string, object> { ["EmergencyId"] = emergency.Id });
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.Conflict)
        {
            // La clave ya se usó con otros datos: no se genera otra en silencio (podría duplicar la emergencia).
            ErrorMessage = ParamedicMessages.EmergencyRequestReused;
        }
        catch (MobileApiException ex) when (ex.Error is not (MobileApiError.Server or MobileApiError.InvalidResponse))
        {
            ErrorMessage = MobileApiErrorMessages.For(ex);
        }
        catch (Exception)
        {
            ErrorMessage = ParamedicMessages.CreateEmergencyFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
