namespace SCI_PH.Mobile.Features.Auth;

/// <summary>
/// Textos del inicio de sesión (personal clínico y pacientes). Los de red y servidor son los comunes de
/// MobileApiErrorMessages.
/// </summary>
public static class AuthMessages
{
    public const string IdentifierRequired = "Ingresa tu correo o usuario.";
    public const string PasswordRequired = "Ingresa tu contraseña.";

    /// <summary>401 del login: no distingue usuario inexistente, contraseña errónea, cuenta bloqueada o inactiva.</summary>
    public const string SignInFailed = "No fue posible iniciar sesión. Revisa tus credenciales e intenta nuevamente.";

    /// <summary>Cuenta válida sin área en la app: ni paciente ni personal con acceso al módulo prehospitalario.</summary>
    public const string NoMobileAccess = "Tu cuenta no tiene acceso a la app móvil de SCI-PH.";

    public const string CodeRequired = "Ingresa el código de 6 dígitos de tu aplicación autenticadora.";
    public const string RecoveryCodeRequired = "Ingresa un código de recuperación.";
    public const string InvalidCode = "El código no es válido. Intenta nuevamente.";
    public const string MfaExpired = "La verificación caducó. Vuelve al inicio de sesión.";
}
