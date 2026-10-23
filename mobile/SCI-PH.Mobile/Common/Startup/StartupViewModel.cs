using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Auth;

namespace SCI_PH.Mobile.Common.Startup;

/// <summary>
/// Primera pantalla de la app: intenta restaurar la sesión clínica guardada (refresh + <c>/me</c> + permiso) y solo
/// entonces decide destino. El área paramédica nunca se muestra antes de que el servidor valide la sesión.
/// </summary>
public sealed class StartupViewModel : BaseViewModel
{
    private readonly IMobileAuthService _auth;
    private readonly IAppNavigator _navigator;
    private bool _started;

    public StartupViewModel(IMobileAuthService auth, IAppNavigator navigator)
    {
        _auth = auth;
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
            ? AuthRoutes.ClinicalArea
            : AppRoutes.Root(AppRoutes.Entry));
    }
}
