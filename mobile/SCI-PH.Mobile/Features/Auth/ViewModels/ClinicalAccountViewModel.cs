using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;

namespace SCI_PH.Mobile.Features.Auth.ViewModels;

/// <summary>
/// Cuenta del personal clínico dentro del área paramédica: quién está dentro y cerrar sesión. El logout local ocurre
/// aunque el servidor no responda, y nunca toca la sesión del paciente.
/// </summary>
public sealed partial class ClinicalAccountViewModel : BaseViewModel
{
    private readonly IMobileAuthService _auth;
    private readonly IClinicalSessionService _session;
    private readonly IAppNavigator _navigator;

    public ClinicalAccountViewModel(IMobileAuthService auth, IClinicalSessionService session, IAppNavigator navigator)
    {
        _auth = auth;
        _session = session;
        _navigator = navigator;
    }

    public string DisplayName => _session.CurrentUser?.UserName ?? string.Empty;

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _auth.LogoutAsync();
        }
        catch (Exception)
        {
            // LogoutAsync ya borra la sesión local pase lo que pase; aquí solo se evita bloquear la salida.
        }
        finally
        {
            IsBusy = false;
        }

        await _navigator.GoToAsync(AppRoutes.Root(AppRoutes.Entry));
    }
}
