namespace SCI_PH.Mobile.Common.Api;

/// <summary>
/// Llamadas JSON al Mobile BFF. Las rutas son relativas a <see cref="MobileApiOptions.BaseAddress"/>
/// (por ejemplo <c>api/mobile/auth/me</c>). Cualquier fallo llega como <see cref="MobileApiException"/>.
/// </summary>
public interface IMobileApiClient
{
    Task<TResponse> GetAsync<TResponse>(string path, CancellationToken cancellationToken = default);

    Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken = default);

    /// <summary>POST sin cuerpo de respuesta (por ejemplo un 204).</summary>
    Task PostAsync<TRequest>(string path, TRequest body, CancellationToken cancellationToken = default);

    /// <summary>Petición ya construida, para cabeceras propias de una llamada.</summary>
    Task<TResponse> SendAsync<TResponse>(HttpRequestMessage request, CancellationToken cancellationToken = default);
}
