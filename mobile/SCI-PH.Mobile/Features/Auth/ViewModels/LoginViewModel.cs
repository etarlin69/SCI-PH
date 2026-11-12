using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Auth.Services;

namespace SCI_PH.Mobile.Features.Auth.ViewModels;

/// <summary>
/// Inicio de sesión con la cuenta de SCI-PH (personal clínico o paciente, ADR-015) contra el Mobile BFF; tras autenticar
/// se abre el área de la cuenta. La contraseña se borra del estado en cuanto se envía, salga bien o mal. Con MFA no se
/// crea sesión: solo se guarda el challenge en memoria y se pasa a verificarlo.
/// </summary>
public sealed partial class LoginViewModel : BaseViewModel
{
    private readonly IMobileAuthService _auth;
    private readonly PendingMfaChallenge _pendingMfa;
    private readonly IAppNavigator _navigator;

    public LoginViewModel(IMobileAuthService auth, PendingMfaChallenge pendingMfa, IAppNavigator navigator)
    {
        _auth = auth;
        _pendingMfa = pendingMfa;
        _navigator = navigator;
    }

    [ObservableProperty]
    private string identifier = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    protected override void OnBusyChanged() => LoginCommand.NotifyCanExecuteChanged();

    private bool CanLogin() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        // Doble envío: el botón se deshabilita, pero un segundo Execute tampoco debe llegar al servidor.
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(Identifier))
        {
            ErrorMessage = AuthMessages.IdentifierRequired;
            return;
        }

        if (string.IsNullOrEmpty(Password))
        {
            ErrorMessage = AuthMessages.PasswordRequired;
            return;
        }

        var submittedPassword = Password;
        Password = string.Empty;
        IsBusy = true;
        try
        {
            var result = await _auth.LoginAsync(Identifier, submittedPassword);
            switch (result.Status)
            {
                case ClinicalSignInStatus.Authenticated:
                    _pendingMfa.Clear();
                    await _navigator.GoToAsync(AuthRoutes.AreaRoute(result.Area));
                    break;
                case ClinicalSignInStatus.MfaRequired:
                    _pendingMfa.Set(result.MfaChallenge!);
                    await _navigator.GoToAsync(AuthRoutes.Mfa);
                    break;
                default:
                    ErrorMessage = AuthMessages.NoMobileAccess;
                    break;
            }
        }
        catch (MobileApiException ex)
        {
            ErrorMessage = MobileApiErrorMessages.For(ex, AuthMessages.SignInFailed);
        }
        catch (Exception)
        {
            ErrorMessage = GenericErrorMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task BackAsync()
    {
        Password = string.Empty;
        return _navigator.GoToAsync("..");
    }
}
