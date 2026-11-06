using System.Net;
using System.Text;
using System.Text.Json;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Tests.Auth;

namespace SCI_PH.Mobile.Tests.Paramedic;

/// <summary>
/// La identificación con QR del Mobile BFF en memoria, con el comportamiento del servidor real (ADR-015): resolve devuelve el
/// candidato sin identificar (404 para cualquier QR no válido, 409 si la emergencia ya tiene paciente); confirm vuelve a
/// validar el QR e identifica una sola vez, idempotente por clave (misma clave y mismo QR: 200 aunque el QR ya venciera;
/// otra combinación: 409). GET de la emergencia refleja el resultado. Puede forzar estados, perder respuestas ya procesadas,
/// agotar el tiempo o retener la respuesta.
/// </summary>
internal sealed class FakeQrIdentificationBff : HttpMessageHandler
{
    public const string PayloadA = "sciphqr:v1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    public const string PayloadB = "sciphqr:v1:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBA";
    public const string TemporaryCode = "NN-20261105-0001";
    public static readonly Guid PatientId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<Guid, (Guid EmergencyId, string Payload)> _confirmKeys = [];

    /// <summary>QR vigentes. Vencer o sustituir uno es quitarlo de aquí.</summary>
    public HashSet<string> Valid { get; } = [PayloadA];

    public List<string> Paths { get; } = [];

    public List<string> Bodies { get; } = [];

    /// <summary>Estados forzados de las próximas respuestas (sin procesar la petición), por orden.</summary>
    public Queue<HttpStatusCode> NextStatuses { get; } = new();

    public TimeSpan? RetryAfter { get; set; }

    /// <summary>Las próximas confirmaciones se procesan y la respuesta se pierde (la red se corta).</summary>
    public int LostResponsesLeft { get; set; }

    /// <summary>Las próximas confirmaciones se procesan y la respuesta no llega a tiempo (timeout del cliente).</summary>
    public int TimeoutsAfterProcessingLeft { get; set; }

    /// <summary>Retiene la próxima respuesta de resolve hasta que el test la libere.</summary>
    public TaskCompletionSource? ResolveGate { get; set; }

    /// <summary>Retiene la próxima respuesta de confirm hasta que el test la libere.</summary>
    public TaskCompletionSource? ConfirmGate { get; set; }

    /// <summary>Si se fija, solo acepta este access token (para el 401 y el refresh del cliente protegido).</summary>
    public Func<string?>? RequiredAccessToken { get; set; }

    public List<string?> AuthorizationHeaders { get; } = [];

    public bool Identified { get; private set; }

    /// <summary>La emergencia tiene paciente por otra vía (otro paramédico, documento o huella).</summary>
    public bool IdentifiedElsewhere { get; set; }

    /// <summary>La emergencia ya no admite identificación (por ejemplo, el hospital la recibió): 409 sin paciente.</summary>
    public bool Closed { get; set; }

    public int Resolves => Paths.Count(p => p.EndsWith("/identify/qr/resolve", StringComparison.Ordinal));

    public int Confirms => Paths.Count(p => p.EndsWith("/identify/qr/confirm", StringComparison.Ordinal));

    public int EmergencyReads => Paths.Count(p => p.StartsWith("GET ", StringComparison.Ordinal));

    public MobileApiClient Client() => new(new HttpClient(this) { BaseAddress = new Uri("http://sciph.test/") });

    public JsonElement Body(int index) => JsonDocument.Parse(Bodies[index]).RootElement;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = request.RequestUri!.Query;
        Paths.Add($"{request.Method.Method} {path}{query}");
        AuthorizationHeaders.Add(request.Headers.Authorization?.Parameter);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        if (body is not null)
        {
            Bodies.Add(body);
        }

        if (RequiredAccessToken is { } required && request.Headers.Authorization?.Parameter != required())
        {
            return FakeMobileBff.Problem(HttpStatusCode.Unauthorized, "Unauthorized (test).");
        }

        var segments = path.Trim('/').Split('/');
        var isResolve = segments is [.., "qr", "resolve"];
        var isConfirm = segments is [.., "qr", "confirm"];
        var gate = isResolve ? ResolveGate : isConfirm ? ConfirmGate : null;
        if (gate is not null)
        {
            if (isResolve)
            {
                ResolveGate = null;
            }
            else
            {
                ConfirmGate = null;
            }

            await gate.Task.WaitAsync(cancellationToken);
        }

        if (NextStatuses.TryDequeue(out var forced))
        {
            var problem = FakeMobileBff.Problem(forced, "Forced (test).");
            if (forced == HttpStatusCode.TooManyRequests && RetryAfter is { } wait)
            {
                problem.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(wait);
            }

            return problem;
        }

        var response = segments switch
        {
            ["api", "mobile", "emergencies", var id] when request.Method == HttpMethod.Get => JsonResponse(HttpStatusCode.OK, Emergency(Guid.Parse(id))),
            ["api", "mobile", "emergencies", var id, "identify", "qr", "resolve"] => Resolve(body!),
            ["api", "mobile", "emergencies", var id, "identify", "qr", "confirm"] => Confirm(Guid.Parse(id), body!),
            _ => FakeMobileBff.Problem(HttpStatusCode.NotFound, "Unknown path (test).")
        };

        if (isConfirm && LostResponsesLeft > 0)
        {
            LostResponsesLeft--;
            throw new HttpRequestException("Connection reset after the request was processed (test).");
        }

        if (isConfirm && TimeoutsAfterProcessingLeft > 0)
        {
            TimeoutsAfterProcessingLeft--;
            throw new TaskCanceledException("The request timed out after it was processed (test).");
        }

        return response;
    }

    private HttpResponseMessage Resolve(string body)
    {
        var payload = JsonDocument.Parse(body).RootElement.GetProperty("payload").GetString();
        if (Identified || IdentifiedElsewhere || Closed)
        {
            return FakeMobileBff.Problem(HttpStatusCode.Conflict, "The emergency is already identified.");
        }

        if (payload is null || !Valid.Contains(payload))
        {
            return FakeMobileBff.Problem(HttpStatusCode.NotFound, "The QR code is not valid.");
        }

        return JsonResponse(HttpStatusCode.OK, new
        {
            displayName = "Ana Pérez",
            dateOfBirth = "1985-03-14",
            document = new { type = "NationalId", value = "***5678" }
        });
    }

    private HttpResponseMessage Confirm(Guid emergencyId, string body)
    {
        var request = JsonDocument.Parse(body).RootElement;
        var key = request.GetProperty("clientRequestId").GetGuid();
        var payload = request.GetProperty("payload").GetString()!;
        if (_confirmKeys.TryGetValue(key, out var existing))
        {
            return existing == (emergencyId, payload)
                ? JsonResponse(HttpStatusCode.OK, Emergency(emergencyId))
                : FakeMobileBff.Problem(HttpStatusCode.Conflict, "Reused key.");
        }

        if (Identified || IdentifiedElsewhere || Closed)
        {
            return FakeMobileBff.Problem(HttpStatusCode.Conflict, "The emergency is already identified.");
        }

        if (!Valid.Contains(payload))
        {
            return FakeMobileBff.Problem(HttpStatusCode.NotFound, "The QR code is not valid.");
        }

        Identified = true;
        IdentificationsCommitted++;
        _confirmKeys[key] = (emergencyId, payload);
        return JsonResponse(HttpStatusCode.OK, Emergency(emergencyId));
    }

    /// <summary>Identificaciones aplicadas por el servidor (nunca más de una).</summary>
    public int IdentificationsCommitted { get; private set; }

    private object Emergency(Guid id) => new
    {
        id,
        temporaryPatientCode = TemporaryCode,
        status = "Created",
        startedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
        chiefComplaint = "Inconsciente",
        patient = Identified || IdentifiedElsewhere ? new { id = PatientId, displayName = "Ana Pérez" } : null,
        activeTransfer = (object?)null
    };

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json")
    };
}
