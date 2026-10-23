using System.Windows.Input;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Features.Auth;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Patient;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;

namespace SCI_PH.Mobile;

/// <summary>
/// Shell único de SCI-PH Mobile. Arranca en la pantalla de inicio, que restaura la sesión clínica guardada y lleva al
/// área paramédica o a la entrada compartida; cada feature registra sus rutas mediante su extensión. El menú lateral es
/// el del área del paciente: solo se habilita con una sesión de paciente, y "Cerrar sesión" la borra y vuelve a la
/// entrada. El flujo paramédico no usa menú: su cierre de sesión está en su primera pantalla.
/// </summary>
public partial class AppShell : Shell
{
    private readonly IPatientSessionService _patientSession;

    public AppShell(IPatientSessionService patientSession, IMobileAuthService clinicalAuth)
    {
        _patientSession = patientSession;
        PatientLogoutCommand = new Command(async () => await LogoutPatientAsync());

        InitializeComponent();
        AuthServiceCollectionExtensions.RegisterAuthRoutes();
        ParamedicServiceCollectionExtensions.RegisterParamedicRoutes();
        PatientServiceCollectionExtensions.RegisterPatientRoutes();

        BindingContext = this;
        _patientSession.SessionChanged += (_, _) => MainThread.BeginInvokeOnMainThread(ApplyPatientSession);
        ApplyPatientSession();

        // El servidor rechazó la renovación de la sesión clínica: vuelta a la entrada. La del paciente no se toca.
        clinicalAuth.SessionExpired += (_, _) =>
            MainThread.BeginInvokeOnMainThread(async () => await GoToAsync(AppRoutes.Root(AppRoutes.Entry)));
    }

    public string PatientDisplayName => _patientSession.CurrentSession?.PatientName ?? string.Empty;

    public ICommand PatientLogoutCommand { get; }

    private void ApplyPatientSession()
    {
        FlyoutBehavior = _patientSession.IsAuthenticated ? FlyoutBehavior.Flyout : FlyoutBehavior.Disabled;
        OnPropertyChanged(nameof(PatientDisplayName));
    }

    private async Task LogoutPatientAsync()
    {
        FlyoutIsPresented = false;
        await _patientSession.ClearSessionAsync();
        await GoToAsync(AppRoutes.Root(AppRoutes.Entry));
    }
}
