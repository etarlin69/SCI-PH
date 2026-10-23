using System.Net;
using System.Net.Http.Headers;
using SCI_PH.Mobile.Common.Authentication;

namespace SCI_PH.Mobile.Common.Api;

/// <summary>
/// Handler del cliente de endpoints protegidos del personal clínico:
/// <list type="number">
/// <item>Añade <c>Authorization: Bearer</c> con el access token en memoria (sin sesión, la petición sale sin él).</item>
/// <item>Ante un 401 pide UN refresh (single-flight en <see cref="IClinicalSessionRefresher"/>) y repite la petición
/// UNA vez con el token nuevo.</item>
/// <item>Si la repetición vuelve a dar 401, cierra la sesión: nunca encadena refresh tras refresh.</item>
/// </list>
/// Los endpoints públicos (login, MFA, refresh, logout) usan otro cliente sin este handler. Nunca registra cabeceras.
/// </summary>
public sealed class AuthenticatedHttpHandler : DelegatingHandler
{
    private readonly IClinicalSessionService _session;
    private readonly IClinicalSessionRefresher _refresher;

    public AuthenticatedHttpHandler(IClinicalSessionService session, IClinicalSessionRefresher refresher)
    {
        _session = session;
        _refresher = refresher;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var accessToken = _session.AccessToken;
        if (accessToken is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        // El cuerpo se copia antes del envío: una petición ya enviada no se reutiliza para el reintento.
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        bool refreshed;
        try
        {
            refreshed = await _refresher.TryRefreshAsync(accessToken, cancellationToken);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        var renewedToken = _session.AccessToken;
        if (!refreshed || renewedToken is null)
        {
            return response;
        }

        response.Dispose();
        using var retry = Clone(request, body, renewedToken);
        var retried = await base.SendAsync(retry, cancellationToken);
        if (retried.StatusCode == HttpStatusCode.Unauthorized)
        {
            await _refresher.EndExpiredSessionAsync(cancellationToken);
        }

        return retried;
    }

    private static HttpRequestMessage Clone(HttpRequestMessage original, byte[]? body, string accessToken)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy
        };

        foreach (var header in original.Headers)
        {
            if (!string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var option in original.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            // Content-Type y demás cabeceras del contenido, una sola vez; la longitud la calcula ByteArrayContent.
            foreach (var header in original.Content!.Headers)
            {
                if (!string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        clone.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return clone;
    }
}
