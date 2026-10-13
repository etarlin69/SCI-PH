using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Dialogs;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Patient.ViewModels;

/// <summary>
/// Paso 2 del acceso: la sesión se crea solo tras la verificación facial 1:1.
/// Simulado hasta que exista <c>POST /api/mobile/auth/face/verify</c>.
/// </summary>
public sealed partial class FaceVerifyViewModel : BaseViewModel
{
    private readonly IPatientSessionService _session;
    private readonly IAppNavigator _navigator;
    private readonly IAppDialogService _dialogs;

    public FaceVerifyViewModel(IPatientSessionService session, IAppNavigator navigator, IAppDialogService dialogs)
    {
        _session = session;
        _navigator = navigator;
        _dialogs = dialogs;
    }

    [ObservableProperty]
    private string statusMessage = "Pendiente de verificación";

    [RelayCommand]
    private async Task VerifyAsync()
    {
        await RunAsync(async () =>
        {
            StatusMessage = "Verificando identidad...";
            await Task.Delay(700);
            await _session.CreateSessionAsync();
            StatusMessage = "Identidad verificada correctamente.";
            await _dialogs.ShowAsync("Verificación correcta", "Identidad confirmada. Sesión iniciada correctamente.", "Continuar");
            await _navigator.GoToAsync(AppRoutes.Root(PatientRoutes.Profile));
        }, GenericErrorMessage);

        if (HasError)
        {
            StatusMessage = "No se pudo verificar la identidad.";
            await _dialogs.ShowAsync("Error de verificación", "No fue posible validar tu identidad. Inténtalo nuevamente.");
        }
    }
}
