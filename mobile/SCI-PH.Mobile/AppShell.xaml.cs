using System.Windows.Input;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Features.Auth;
using SCI_PH.Mobile.Features.Paramedic;

namespace SCI_PH.Mobile;

/// <summary>
/// Shell único de SCI-PH Mobile. Arranca en la pantalla de inicio, que restaura la sesión guardada y lleva al área de la
/// cuenta (paramédica o del paciente) o a la entrada compartida; cada feature registra sus rutas mediante su extensión.
/// Hay una sola sesión (<see cref="IClinicalSessionService"/>): el menú lateral es el del área del paciente y solo se
/// habilita cuando la sesión es de un paciente; "Cerrar sesión" la cierra y vuelve a la entrada. El flujo paramédico no
/// usa menú: su cierre de sesión está en su primera pantalla.
/// </summary>
public partial class AppShell : Shell
{
    private readonly IClinicalSessionService _session;
    private readonly IMobileAuthService _auth;

    public AppShell(IClinicalSessionService session, IMobileAuthService auth)
    {
        _session = session;
        _auth = auth;
        PatientLogoutCommand = new Command(async () => await LogoutPatientAsync());

        InitializeComponent();
        AuthServiceCollectionExtensions.RegisterAuthRoutes();
        ParamedicServiceCollectionExtensions.RegisterParamedicRoutes();

        BindingContext = this;
        _session.SessionChanged += (_, _) => MainThread.BeginInvokeOnMainThread(ApplySession);
        ApplySession();

        // El servidor rechazó la renovación de la sesión (de cualquier área): vuelta a la entrada.
        auth.SessionExpired += (_, _) =>
            MainThread.BeginInvokeOnMainThread(async () => await GoToAsync(AppRoutes.Root(AppRoutes.Entry)));
    }

    public string PatientDisplayName => _session.Area == AccountArea.Patient ? _session.CurrentUser?.UserName ?? string.Empty : string.Empty;

    public ICommand PatientLogoutCommand { get; }

    private void ApplySession()
    {
        FlyoutBehavior = _session.Area == AccountArea.Patient ? FlyoutBehavior.Flyout : FlyoutBehavior.Disabled;
        OnPropertyChanged(nameof(PatientDisplayName));
    }

    private async Task LogoutPatientAsync()
    {
        FlyoutIsPresented = false;
        try
        {
            await _auth.LogoutAsync();
        }
        catch (Exception)
        {
            // LogoutAsync ya borra la sesión local pase lo que pase; aquí solo se evita bloquear la salida.
        }

        await GoToAsync(AppRoutes.Root(AppRoutes.Entry));
    }
}
