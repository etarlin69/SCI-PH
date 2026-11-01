using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Auth.Services;

namespace SCI_PH.Mobile.Features.Auth.ViewModels;

/// <summary>
/// Segundo factor con <c>POST /api/mobile/auth/mfa/verify</c>: código TOTP de 6 dígitos o, alternativamente, un código
/// de recuperación. Usa el challenge del login; los códigos se borran del estado al enviarlos. Un código erróneo permite
/// reintentar con el mismo challenge; si caduca, hay que volver a iniciar sesión.
/// </summary>
public sealed partial class MfaViewModel : BaseViewModel
{
    public const int CodeLength = 6;
    public const int RecoveryCodeMaxLength = 32;

    private readonly IMobileAuthService _auth;
    private readonly PendingMfaChallenge _pendingMfa;
    private readonly IAppNavigator _navigator;
    private readonly TimeProvider _clock;

    public MfaViewModel(IMobileAuthService auth, PendingMfaChallenge pendingMfa, IAppNavigator navigator, TimeProvider clock)
    {
        _auth = auth;
        _pendingMfa = pendingMfa;
        _navigator = navigator;
        _clock = clock;
    }

    [ObservableProperty]
    private string code = string.Empty;

    [ObservableProperty]
    private string recoveryCode = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuthenticatorMode))]
    private bool isRecoveryMode;

    /// <summary>El challenge ya no sirve (caducado o consumido): solo queda volver al login.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEnterCode))]
    private bool isChallengeClosed;

    public bool IsAuthenticatorMode => !IsRecoveryMode;

    public bool CanEnterCode => !IsChallengeClosed;

    protected override void OnBusyChanged() => VerifyCommand.NotifyCanExecuteChanged();

    partial void OnIsChallengeClosedChanged(bool value) => VerifyCommand.NotifyCanExecuteChanged();

    private bool CanVerify() => !IsBusy && !IsChallengeClosed;

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAsync()
    {
        if (IsBusy || IsChallengeClosed)
        {
            return;
        }

        ErrorMessage = null;
        var challenge = _pendingMfa.Current;
        if (challenge is null || IsExpired(challenge.ExpiresAtUtc))
        {
            CloseChallenge(AuthMessages.MfaExpired);
            return;
        }

        var useRecoveryCode = IsRecoveryMode;
        var value = useRecoveryCode ? RecoveryCode.Trim() : Code.Replace(" ", string.Empty);
        var validationError = useRecoveryCode ? ValidateRecoveryCode(value) : ValidateCode(value);
        if (validationError is not null)
        {
            ErrorMessage = validationError;
            return;
        }

        Code = string.Empty;
        RecoveryCode = string.Empty;
        IsBusy = true;
        try
        {
            var result = useRecoveryCode
                ? await _auth.VerifyRecoveryCodeAsync(challenge, value)
                : await _auth.VerifyMfaCodeAsync(challenge, value);

            if (result.Status == ClinicalSignInStatus.Authenticated)
            {
                _pendingMfa.Clear();
                await _navigator.GoToAsync(AuthRoutes.AreaRoute(result.Area));
            }
            else
            {
                // El servidor emitió la sesión pero la cuenta no tiene área en la app: el challenge quedó consumido.
                CloseChallenge(AuthMessages.NoMobileAccess);
            }
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.Unauthorized && IsExpired(challenge.ExpiresAtUtc))
        {
            CloseChallenge(AuthMessages.MfaExpired);
        }
        catch (MobileApiException ex)
        {
            ErrorMessage = MobileApiErrorMessages.For(ex, AuthMessages.InvalidCode);
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
    private void ToggleRecoveryMode()
    {
        IsRecoveryMode = !IsRecoveryMode;
        Code = string.Empty;
        RecoveryCode = string.Empty;
        if (!IsChallengeClosed)
        {
            ErrorMessage = null;
        }
    }

    [RelayCommand]
    private Task BackToLoginAsync()
    {
        ForgetChallenge();
        return _navigator.GoToAsync("..");
    }

    /// <summary>
    /// Al salir de la verificación por cualquier vía (botón, flecha de la barra de título, atrás del sistema) el challenge y
    /// los códigos dejan de estar en memoria.
    /// </summary>
    public void ForgetChallenge()
    {
        _pendingMfa.Clear();
        Code = string.Empty;
        RecoveryCode = string.Empty;
    }

    private bool IsExpired(DateTimeOffset expiresAtUtc) => _clock.GetUtcNow() >= expiresAtUtc;

    private void CloseChallenge(string message)
    {
        _pendingMfa.Clear();
        Code = string.Empty;
        RecoveryCode = string.Empty;
        IsChallengeClosed = true;
        ErrorMessage = message;
    }

    private static string? ValidateCode(string value) =>
        value.Length == CodeLength && value.All(char.IsAsciiDigit) ? null : AuthMessages.CodeRequired;

    private static string? ValidateRecoveryCode(string value) =>
        value.Length is > 0 and <= RecoveryCodeMaxLength ? null : AuthMessages.RecoveryCodeRequired;
}
