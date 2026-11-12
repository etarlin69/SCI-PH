using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Dialogs;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Auth.Services;

namespace SCI_PH.Mobile.Features.Auth.ViewModels;

/// <summary>
/// "Seguridad de la cuenta": cambiar la contraseña o cerrar la sesión en todos los dispositivos. La nueva contraseña se
/// comprueba aquí con la misma política del servidor (10 caracteres, mayúscula, minúscula, número y símbolo) para no enviar
/// una que seguro se rechazará; el servidor decide. Tras el éxito la sesión termina (el servidor revocó todas) y se vuelve
/// a la entrada. Las contraseñas solo viven en el formulario y se borran siempre después de enviarlas.
/// </summary>
public sealed partial class AccountSecurityViewModel : BaseViewModel
{
    public const int MinimumPasswordLength = 10;

    public const string PasswordChanged = "Tu contraseña se cambió. Inicia sesión de nuevo con la nueva contraseña.";
    public const string SessionsRevoked = "Se cerró la sesión en todos los dispositivos. Inicia sesión de nuevo.";
    public const string RequiredFields = "Completa la contraseña actual, la nueva y su confirmación.";
    public const string Mismatch = "La confirmación no coincide con la nueva contraseña.";
    public const string SameAsCurrent = "La nueva contraseña debe ser distinta de la actual.";
    public const string WeakPassword =
        "La nueva contraseña debe tener al menos 10 caracteres, con mayúscula, minúscula, número y símbolo.";
    public const string Rejected = "La contraseña actual no es correcta o la nueva no cumple los requisitos.";

    private readonly IAccountSecurityService _security;
    private readonly IAppNavigator _navigator;
    private readonly IAppDialogService _dialogs;

    public AccountSecurityViewModel(IAccountSecurityService security, IAppNavigator navigator, IAppDialogService dialogs)
    {
        _security = security;
        _navigator = navigator;
        _dialogs = dialogs;
    }

    [ObservableProperty]
    private string currentPassword = string.Empty;

    [ObservableProperty]
    private string newPassword = string.Empty;

    [ObservableProperty]
    private string confirmPassword = string.Empty;

    public static string? Validate(string current, string next, string confirm)
    {
        if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(next) || string.IsNullOrEmpty(confirm))
        {
            return RequiredFields;
        }

        if (next != confirm)
        {
            return Mismatch;
        }

        if (next == current)
        {
            return SameAsCurrent;
        }

        var strong = next.Length >= MinimumPasswordLength
            && next.Any(char.IsUpper) && next.Any(char.IsLower) && next.Any(char.IsDigit) && next.Any(c => !char.IsLetterOrDigit(c));
        return strong ? null : WeakPassword;
    }

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (Validate(CurrentPassword, NewPassword, ConfirmPassword) is { } invalid)
        {
            ErrorMessage = invalid;
            return;
        }

        var (current, next) = (CurrentPassword, NewPassword);
        ClearFields();
        await RunAsync(async () =>
        {
            await _security.ChangePasswordAsync(current, next);
            await _dialogs.ShowAsync("Contraseña cambiada", PasswordChanged);
            await _navigator.GoToAsync(AppRoutes.Root(AppRoutes.Entry));
        }, MessageFor);
    }

    [RelayCommand]
    private async Task RevokeAllSessionsAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ClearFields();
        await RunAsync(async () =>
        {
            await _security.RevokeAllSessionsAsync();
            await _dialogs.ShowAsync("Sesiones cerradas", SessionsRevoked);
            await _navigator.GoToAsync(AppRoutes.Root(AppRoutes.Entry));
        }, MessageFor);
    }

    private void ClearFields()
    {
        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
    }

    private static string MessageFor(Exception exception) => exception switch
    {
        MobileApiException { Error: MobileApiError.BadRequest } => Rejected,
        MobileApiException api => MobileApiErrorMessages.For(api),
        _ => GenericErrorMessage
    };
}
