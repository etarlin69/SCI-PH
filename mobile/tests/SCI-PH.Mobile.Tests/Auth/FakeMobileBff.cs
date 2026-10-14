using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SCI_PH.Mobile.Common.Authentication;

namespace SCI_PH.Mobile.Tests.Auth;

/// <summary>
/// Mobile BFF en memoria con el contrato de api/mobile/auth: login (con o sin MFA), mfa/verify, refresh con rotación
/// (reutilizar un refresh viejo revoca la familia), logout, /me y un endpoint protegido de prueba. Los valores son
/// ficticios y solo existen en los tests.
/// </summary>
internal sealed record ReceivedRequest(string Method, List<string> Authorization, List<string> ContentType, string? Body);

internal sealed class FakeMobileBff : HttpMessageHandler
{
    public const string ProtectedPath = "/api/mobile/test/protected";
    public const string Identifier = "paramedic@sci-ph.test";
    public const string Password = "test-password-not-real";
    public const string TotpCode = "123456";
    public const string RecoveryCode = "TEST-RECOVERY-01";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly HashSet<string> _usedRefreshTokens = [];
    private readonly string _mfaChallengeToken = "mfa-challenge-" + Guid.NewGuid().ToString("N");
    private int _generation;
    private int _staleProtectedArrivals;
    private TaskCompletionSource? _staleProtectedBarrier;

    public bool RequireMfa { get; set; }

    public DateTimeOffset MfaChallengeExpiresAtUtc { get; set; } = DateTimeOffset.UtcNow.AddMinutes(5);

    public List<string> Roles { get; set; } = ["Paramedic"];

    public List<string> Permissions { get; set; } = [ClinicalPermissions.EmergenciesOperate, "VitalSigns.Write"];

    /// <summary>El servidor rechaza cualquier access token en el endpoint protegido (para el segundo 401).</summary>
    public bool RejectAllAccessTokens { get; set; }

    /// <summary>Rutas que fallan sin respuesta, como un servidor inalcanzable.</summary>
    public HashSet<string> UnreachablePaths { get; } = [];

    /// <summary>Retiene /refresh hasta que el test lo libera.</summary>
    public TaskCompletionSource? RefreshGate { get; set; }

    public string? AccessToken { get; private set; }

    public string? RefreshToken { get; private set; }

    public int LoginCalls { get; private set; }

    public int RefreshCalls { get; private set; }

    public int LogoutCalls { get; private set; }

    public int MeCalls { get; private set; }

    public int ProtectedCalls { get; private set; }

    public List<JsonElement> MfaVerifyBodies { get; } = [];

    /// <summary>Lo que llegó al endpoint protegido en cada intento.</summary>
    public List<ReceivedRequest> ProtectedRequests { get; } = [];

    /// <summary>Emite una sesión directamente, como si ya hubiera habido un login.</summary>
    public (string AccessToken, string RefreshToken) IssueSession()
    {
        lock (_gate)
        {
            return Issue();
        }
    }

    /// <summary>El access token actual deja de ser válido (caducó); el refresh sigue vigente.</summary>
    public void ExpireAccessToken()
    {
        lock (_gate)
        {
            AccessToken = "expired-" + Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>Revoca toda la sesión en el servidor (por ejemplo, desde la web o por caducidad).</summary>
    public void RevokeSession()
    {
        lock (_gate)
        {
            AccessToken = null;
            RefreshToken = null;
        }
    }

    /// <summary>Las peticiones protegidas con un token viejo esperan a que lleguen <paramref name="count"/> antes del 401.</summary>
    public void HoldStaleRequestsUntil(int count)
    {
        _staleProtectedBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _staleProtectedArrivals = count;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (UnreachablePaths.Contains(path))
        {
            throw new HttpRequestException("Connection refused (test).");
        }

        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return (request.Method.Method, path) switch
        {
            ("POST", "/api/mobile/auth/login") => Login(body!),
            ("POST", "/api/mobile/auth/mfa/verify") => VerifyMfa(body!),
            ("POST", "/api/mobile/auth/refresh") => await RefreshAsync(body!),
            ("POST", "/api/mobile/auth/logout") => Logout(body!),
            ("GET", "/api/mobile/auth/me") => Me(request),
            (_, ProtectedPath) => await ProtectedAsync(request, body),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
    }

    private HttpResponseMessage Login(string body)
    {
        LoginCalls++;
        var json = JsonDocument.Parse(body).RootElement;
        if (json.GetProperty("identifier").GetString() != Identifier || json.GetProperty("password").GetString() != Password)
        {
            return Problem(HttpStatusCode.Unauthorized, "Authentication failed.");
        }

        if (RequireMfa)
        {
            return Ok(new
            {
                mfaRequired = true,
                session = (object?)null,
                mfaChallenge = new { token = _mfaChallengeToken, expiresAtUtc = MfaChallengeExpiresAtUtc }
            });
        }

        lock (_gate)
        {
            return Ok(new { mfaRequired = false, session = Session(Issue()), mfaChallenge = (object?)null });
        }
    }

    private HttpResponseMessage VerifyMfa(string body)
    {
        var json = JsonDocument.Parse(body).RootElement;
        MfaVerifyBodies.Add(json.Clone());

        var code = OptionalString(json, "code");
        var recovery = OptionalString(json, "recoveryCode");
        var valid = OptionalString(json, "challengeToken") == _mfaChallengeToken
            && DateTimeOffset.UtcNow < MfaChallengeExpiresAtUtc
            && ((code == TotpCode && recovery is null) || (recovery == RecoveryCode && code is null));
        if (!valid)
        {
            return Problem(HttpStatusCode.Unauthorized, "Authentication failed.");
        }

        lock (_gate)
        {
            return Ok(Session(Issue()));
        }
    }

    private async Task<HttpResponseMessage> RefreshAsync(string body)
    {
        RefreshCalls++;
        if (RefreshGate is { } gate)
        {
            await gate.Task;
        }

        var presented = JsonDocument.Parse(body).RootElement.GetProperty("refreshToken").GetString()!;
        lock (_gate)
        {
            if (presented != RefreshToken)
            {
                if (_usedRefreshTokens.Contains(presented))
                {
                    // Reutilización: el servidor revoca toda la familia.
                    AccessToken = null;
                    RefreshToken = null;
                }

                return Problem(HttpStatusCode.Unauthorized, "Authentication failed.");
            }

            _usedRefreshTokens.Add(presented);
            return Ok(Session(Issue()));
        }
    }

    private HttpResponseMessage Logout(string body)
    {
        LogoutCalls++;
        var presented = JsonDocument.Parse(body).RootElement.GetProperty("refreshToken").GetString();
        lock (_gate)
        {
            if (presented == RefreshToken)
            {
                AccessToken = null;
                RefreshToken = null;
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    private HttpResponseMessage Me(HttpRequestMessage request)
    {
        MeCalls++;
        if (!HasValidBearer(request))
        {
            return Problem(HttpStatusCode.Unauthorized, "Unauthorized");
        }

        return Ok(new
        {
            id = Guid.Parse("7f1d2a4e-0000-4000-8000-000000000001"),
            userName = Identifier,
            email = Identifier,
            roles = Roles,
            permissions = Permissions
        });
    }

    private async Task<HttpResponseMessage> ProtectedAsync(HttpRequestMessage request, string? body)
    {
        lock (_gate)
        {
            ProtectedCalls++;
            ProtectedRequests.Add(new ReceivedRequest(
                request.Method.Method,
                request.Headers.TryGetValues("Authorization", out var authorization) ? authorization.ToList() : [],
                request.Content?.Headers.TryGetValues("Content-Type", out var contentType) == true ? contentType.ToList() : [],
                body));
        }

        if (HasValidBearer(request) && !RejectAllAccessTokens)
        {
            return Ok(new { ok = true });
        }

        if (_staleProtectedBarrier is { } barrier)
        {
            if (Interlocked.Decrement(ref _staleProtectedArrivals) == 0)
            {
                barrier.TrySetResult();
            }

            await barrier.Task;
        }

        return Problem(HttpStatusCode.Unauthorized, "Unauthorized");
    }

    private bool HasValidBearer(HttpRequestMessage request)
    {
        lock (_gate)
        {
            return request.Headers.Authorization is { Scheme: "Bearer" } auth
                && AccessToken is not null
                && auth.Parameter == AccessToken;
        }
    }

    private (string AccessToken, string RefreshToken) Issue()
    {
        _generation++;
        AccessToken = $"access-{_generation}-{Guid.NewGuid():N}";
        RefreshToken = $"refresh-{_generation}-{Guid.NewGuid():N}";
        return (AccessToken, RefreshToken);
    }

    private object Session((string AccessToken, string RefreshToken) tokens) => new
    {
        accessToken = tokens.AccessToken,
        accessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15),
        refreshToken = tokens.RefreshToken,
        refreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30),
        user = new { id = Guid.NewGuid(), userName = Identifier, email = Identifier, roles = Roles, permissions = Permissions }
    };

    private static string? OptionalString(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static HttpResponseMessage Ok(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json")
    };

    internal static HttpResponseMessage Problem(HttpStatusCode status, string title)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { type = "about:blank", title, status = (int)status, detail = "Internal detail (test)." }, Json),
                Encoding.UTF8)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/problem+json");
        return response;
    }
}
