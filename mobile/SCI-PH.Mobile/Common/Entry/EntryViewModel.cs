using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Auth;

namespace SCI_PH.Mobile.Common.Entry;

/// <summary>
/// Entrada compartida: un solo inicio de sesión con la cuenta de SCI-PH para el personal clínico y para los pacientes
/// (ADR-015). Si ya hay una sesión verificada, vuelve directamente a su área. El login se apila sobre la entrada, así que
/// "Atrás" vuelve aquí. No hay acceso por QR ni por rostro: ninguno de los dos inicia una sesión.
/// </summary>
public partial class EntryViewModel : BaseViewModel
{
    private readonly IAppNavigator _navigator;
    private readonly IClinicalSessionService _session;

    public EntryViewModel(IAppNavigator navigator, IClinicalSessionService session)
    {
        _navigator = navigator;
        _session = session;
    }

    [RelayCommand]
    private Task SignInAsync() =>
        _navigator.GoToAsync(_session.Area == AccountArea.None ? AuthRoutes.Login : AuthRoutes.AreaRoute(_session.Area));
}
