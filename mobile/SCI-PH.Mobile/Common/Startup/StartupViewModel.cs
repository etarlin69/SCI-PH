using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Auth;

namespace SCI_PH.Mobile.Common.Startup;

/// <summary>
/// Primera pantalla de la app: intenta restaurar la sesión guardada (refresh + <c>/me</c> + área) y solo entonces decide
/// destino: el área de la cuenta (paramédica o del paciente) o la entrada. Ningún área se muestra antes de que el
/// servidor valide la sesión.
/// </summary>
public sealed class StartupViewModel : BaseViewModel
{
    private readonly IMobileAuthService _auth;
    private readonly IClinicalSessionService _session;
    private readonly IAppNavigator _navigator;
    private bool _started;

    public StartupViewModel(IMobileAuthService auth, IClinicalSessionService session, IAppNavigator navigator)
    {
        _auth = auth;
        _session = session;
        _navigator = navigator;
    }

    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        IsBusy = true;
        ClinicalSessionRestore result;
        try
        {
            result = await _auth.RestoreSessionAsync();
        }
        catch (Exception)
        {
            result = ClinicalSessionRestore.Unavailable;
        }
        finally
        {
            IsBusy = false;
        }

        await _navigator.GoToAsync(result == ClinicalSessionRestore.Restored
            ? AuthRoutes.AreaRoute(_session.Area)
            : AppRoutes.Root(AppRoutes.Entry));
    }
}
