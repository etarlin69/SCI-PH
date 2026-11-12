using System.Net;

namespace SCI_PH.Mobile.Common.Api;

/// <summary>Tipo de fallo de una llamada al Mobile BFF, independiente del texto que verá el usuario.</summary>
public enum MobileApiError
{
    /// <summary>No hubo respuesta: sin red, DNS, conexión rechazada o servidor caído.</summary>
    Network,
    Timeout,

    /// <summary>La app no tiene dirección del Mobile BFF (Release sin <c>MobileBffBaseUrl</c>).</summary>
    NotConfigured,
    BadRequest,
    Unauthorized,
    Forbidden,
    Conflict,
    TooManyRequests,
    Server,

    /// <summary>Respuesta inesperada: estado no previsto o cuerpo que no corresponde al contrato.</summary>
    InvalidResponse
}

/// <summary>
/// Fallo de una llamada al Mobile BFF. No incluye el cuerpo de la respuesta ni datos de la petición; los ViewModels
/// muestran siempre el texto de <see cref="MobileApiErrorMessages"/>, nunca <see cref="Exception.Message"/>.
/// </summary>
public sealed class MobileApiException : Exception
{
    public MobileApiException(MobileApiError error, HttpStatusCode? statusCode = null, TimeSpan? retryAfter = null, Exception? innerException = null)
        : base($"Mobile BFF call failed: {error}.", innerException)
    {
        Error = error;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public MobileApiError Error { get; }

    public HttpStatusCode? StatusCode { get; }

    /// <summary>Espera indicada por <c>Retry-After</c> en un 429, si la hubo.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>Fallo de conectividad: el servidor no llegó a responder.</summary>
    public bool IsConnectivityFailure => Error is MobileApiError.Network or MobileApiError.Timeout;
}
