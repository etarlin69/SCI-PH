using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

namespace SCI_PH.PatientPrototype.Features.Patient.Auth;

public partial class FaceVerifyPage : ContentPage
{
    private readonly IPatientSessionService _sessionService;

    public FaceVerifyPage(IPatientSessionService sessionService)
    {
        InitializeComponent();
        _sessionService = sessionService;
    }

    private async void OnVerifyFaceClicked(object sender, EventArgs e)
    {
        try
        {
            StatusLabel.Text = "Verificando identidad...";

            await Task.Delay(700);

            await _sessionService.CreateMockSessionAsync();

            StatusLabel.Text = "Identidad verificada correctamente.";

            await DisplayAlert(
                "Verificación correcta",
                "Identidad confirmada. Sesión iniciada correctamente.",
                "Continuar");

            // Habilitamos el menú después de autenticar al paciente.
            Shell.Current.FlyoutBehavior = FlyoutBehavior.Flyout;

            // Entramos al área autenticada.
            await Shell.Current.GoToAsync("//Profile");
        }
        catch
        {
            StatusLabel.Text = "No se pudo verificar la identidad.";

            await DisplayAlert(
                "Error de verificación",
                "No fue posible validar tu identidad. Inténtalo nuevamente.",
                "OK");
        }
    }
}