namespace SCI_PH.Mobile.Features.Auth;

/// <summary>Textos del acceso del personal clínico. Los de red y servidor son los comunes de MobileApiErrorMessages.</summary>
public static class AuthMessages
{
    public const string IdentifierRequired = "Ingresa tu correo o usuario.";
    public const string PasswordRequired = "Ingresa tu contraseña.";

    /// <summary>401 del login: no distingue usuario inexistente, contraseña errónea, cuenta bloqueada o inactiva.</summary>
    public const string SignInFailed = "No fue posible iniciar sesión. Revisa tus credenciales e intenta nuevamente.";

    public const string NoEmergencyAccess = "Tu cuenta no tiene acceso al módulo prehospitalario.";

    public const string CodeRequired = "Ingresa el código de 6 dígitos de tu aplicación autenticadora.";
    public const string RecoveryCodeRequired = "Ingresa un código de recuperación.";
    public const string InvalidCode = "El código no es válido. Intenta nuevamente.";
    public const string MfaExpired = "La verificación caducó. Vuelve al inicio de sesión.";
}
