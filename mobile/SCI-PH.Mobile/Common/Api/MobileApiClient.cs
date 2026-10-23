using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SCI_PH.Mobile.Common.Api;

/// <summary>
/// Cliente JSON del Mobile BFF sobre un <see cref="HttpClient"/>. Traduce cualquier fallo a
/// <see cref="MobileApiException"/> según el estado HTTP; el cuerpo ProblemDetails de los errores no se lee ni se
/// expone. Nunca registra peticiones, respuestas ni cabeceras.
/// </summary>
public sealed class MobileApiClient : IMobileApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public MobileApiClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// <see cref="HttpClient"/> con la dirección y el timeout de <paramref name="options"/>. En Android el
    /// <see cref="HttpClientHandler"/> por defecto es el nativo, que aplica la network security config de la app.
    /// </summary>
    public static HttpClient CreateHttpClient(MobileApiOptions options, DelegatingHandler? handler = null)
    {
        HttpMessageHandler pipeline = new HttpClientHandler();
        if (handler is not null)
        {
            handler.InnerHandler = pipeline;
            pipeline = handler;
        }

        var http = new HttpClient(pipeline) { Timeout = options.Timeout };
        if (options.BaseAddress is not null)
        {
            http.BaseAddress = options.BaseAddress.AbsoluteUri.EndsWith('/')
                ? options.BaseAddress
                : new Uri(options.BaseAddress.AbsoluteUri + "/");
        }

        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    public Task<TResponse> GetAsync<TResponse>(string path, CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

    public Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken = default) =>
        SendAsync<TResponse>(JsonPost(path, body), cancellationToken);

    public async Task PostAsync<TRequest>(string path, TRequest body, CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(JsonPost(path, body), cancellationToken);
    }

    public async Task<TResponse> SendAsync<TResponse>(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(request, cancellationToken);
        try
        {
            return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken)
                ?? throw new MobileApiException(MobileApiError.InvalidResponse, response.StatusCode);
        }
        catch (JsonException ex)
        {
            throw new MobileApiException(MobileApiError.InvalidResponse, response.StatusCode, innerException: ex);
        }
        catch (NotSupportedException ex)
        {
            // Content-Type que no es JSON.
            throw new MobileApiException(MobileApiError.InvalidResponse, response.StatusCode, innerException: ex);
        }
    }

    private static HttpRequestMessage JsonPost<TRequest>(string path, TRequest body) =>
        new(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: JsonOptions) };

    private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            if (_http.BaseAddress is null)
            {
                throw new MobileApiException(MobileApiError.NotConfigured);
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (MobileApiException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                // HttpClient.Timeout.
                throw new MobileApiException(MobileApiError.Timeout, innerException: ex);
            }
            catch (Exception ex)
            {
                // Sin respuesta: red, DNS, conexión rechazada, TLS o tráfico HTTP bloqueado por la plataforma.
                throw new MobileApiException(MobileApiError.Network, innerException: ex);
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            using (response)
            {
                throw ToException(response);
            }
        }
    }

    private static MobileApiException ToException(HttpResponseMessage response)
    {
        var status = response.StatusCode;
        var error = status switch
        {
            HttpStatusCode.BadRequest => MobileApiError.BadRequest,
            HttpStatusCode.Unauthorized => MobileApiError.Unauthorized,
            HttpStatusCode.Forbidden => MobileApiError.Forbidden,
            HttpStatusCode.Conflict => MobileApiError.Conflict,
            HttpStatusCode.TooManyRequests => MobileApiError.TooManyRequests,
            >= HttpStatusCode.InternalServerError => MobileApiError.Server,
            _ => MobileApiError.InvalidResponse
        };

        return new MobileApiException(error, status, error == MobileApiError.TooManyRequests ? RetryAfter(response) : null);
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        return header?.Date is { } date ? date - DateTimeOffset.UtcNow : null;
    }
}
