namespace SCI_PH.Mobile.Common.Api;

/// <summary>
/// Textos para el usuario a partir de <see cref="MobileApiException.Error"/>. Nunca muestran ProblemDetails, el estado
/// HTTP ni el detalle de la excepción. Un fallo de red nunca se presenta como credenciales incorrectas.
/// </summary>
public static class MobileApiErrorMessages
{
    public const string Network = "No fue posible conectar con SCI-PH. Intenta nuevamente.";
    public const string NotConfigured = "La app no tiene configurado el servidor de SCI-PH.";
    public const string BadRequest = "La solicitud no es válida. Revisa los datos e intenta nuevamente.";
    public const string Unauthorized = "Tu sesión no es válida. Inicia sesión de nuevo.";
    public const string Forbidden = "No tienes acceso a esta función.";
    public const string Conflict = "La operación entró en conflicto con otro cambio. Intenta nuevamente.";
    public const string TooManyRequests = "Demasiados intentos. Espera un momento e intenta nuevamente.";
    public const string Server = "SCI-PH no pudo completar la solicitud. Intenta nuevamente más tarde.";

    /// <param name="unauthorizedMessage">Texto para un 401 en su contexto (por ejemplo, inicio de sesión fallido).</param>
    public static string For(MobileApiException exception, string? unauthorizedMessage = null) => exception.Error switch
    {
        MobileApiError.Network or MobileApiError.Timeout => Network,
        MobileApiError.NotConfigured => NotConfigured,
        MobileApiError.BadRequest => BadRequest,
        MobileApiError.Unauthorized => unauthorizedMessage ?? Unauthorized,
        MobileApiError.Forbidden => Forbidden,
        MobileApiError.Conflict => Conflict,
        MobileApiError.TooManyRequests => TooManyRequestsFor(exception.RetryAfter),
        _ => Server
    };

    private static string TooManyRequestsFor(TimeSpan? retryAfter)
    {
        if (retryAfter is not { } wait || wait <= TimeSpan.Zero)
        {
            return TooManyRequests;
        }

        var seconds = (int)Math.Ceiling(wait.TotalSeconds);
        return seconds < 60
            ? $"Demasiados intentos. Espera {seconds} segundos e intenta nuevamente."
            : $"Demasiados intentos. Espera {(int)Math.Ceiling(seconds / 60d)} minutos e intenta nuevamente.";
    }
}
