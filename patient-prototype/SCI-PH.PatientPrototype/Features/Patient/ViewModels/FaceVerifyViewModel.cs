using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

namespace SCI_PH.PatientPrototype.Features.Patient.ViewModels;

/// <summary>
/// Paso 2 del acceso: la sesión se crea solo tras la verificación facial 1:1.
/// Simulado hasta que exista <c>POST /api/mobile/auth/face/verify</c>.
/// </summary>
public sealed partial class FaceVerifyViewModel : PatientViewModel
{
    private readonly IPatientSessionService _session;
    private readonly IPatientNavigator _navigator;
    private readonly IPatientDialogService _dialogs;

    public FaceVerifyViewModel(IPatientSessionService session, IPatientNavigator navigator, IPatientDialogService dialogs)
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
        await RunAsync(async cancellationToken =>
        {
            StatusMessage = "Verificando identidad...";
            await Task.Delay(700, cancellationToken);
            await _session.CreateSessionAsync(cancellationToken);
            StatusMessage = "Identidad verificada correctamente.";
            await _dialogs.ShowAsync("Verificación correcta", "Identidad confirmada. Sesión iniciada correctamente.", "Continuar");
            await _navigator.GoToAsync(PatientRoutes.Root(PatientRoutes.Profile));
        });

        if (HasError)
        {
            StatusMessage = "No se pudo verificar la identidad.";
            await _dialogs.ShowAsync("Error de verificación", "No fue posible validar tu identidad. Inténtalo nuevamente.");
        }
    }
}
