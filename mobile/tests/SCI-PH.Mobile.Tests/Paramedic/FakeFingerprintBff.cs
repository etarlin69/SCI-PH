using System.Net;
using System.Text;
using System.Text.Json;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Tests.Auth;

namespace SCI_PH.Mobile.Tests.Paramedic;

/// <summary>
/// La identificación por huella del Mobile BFF en memoria, con el comportamiento del servidor real: lectores activos,
/// apertura idempotente por clave (201/200/409, una sesión activa por emergencia), estado de la sesión (lo que el lector
/// "responde" se guiona con <see cref="Script"/>), confirmación idempotente que identifica la emergencia con el paciente de
/// la sesión (la app no envía paciente) y cancelación (409 si ya se confirmó). Puede fallar sin respuesta y forzar un estado.
/// </summary>
internal sealed class FakeFingerprintBff : HttpMessageHandler
{
    public static readonly Guid ReaderA = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    public static readonly Guid ReaderB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    public static readonly Guid PatientId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class Session
    {
        public Guid Id { get; init; }
        public Guid EmergencyId { get; init; }
        public Guid ReaderId { get; init; }
        public string Status { get; set; } = "Pending";
        public string? Failure { get; set; }
        public bool WithCandidate { get; set; } = true;
        public DateTimeOffset ExpiresAtUtc { get; init; }
    }

    private readonly List<Session> _sessions = [];
    private readonly Dictionary<Guid, (string Payload, Guid SessionId)> _openKeys = [];
    private readonly Dictionary<Guid, (string Payload, Guid EmergencyId)> _confirmKeys = [];

    public List<string> Paths { get; } = [];

    public List<string> Bodies { get; } = [];

    /// <summary>Lectores que devuelve el servidor (por defecto, uno solo).</summary>
    public List<Guid> Readers { get; } = [ReaderA];

    /// <summary>
    /// Lo que el lector va respondiendo: cada GET de la sesión toma el siguiente estado ("Pending", "Matched",
    /// "NoMatch", "Failed:LowQuality", "Failed:SensorError", "Expired", "Matched:NoCandidate"...). Vacío: no cambia.
    /// </summary>
    public Queue<string> Script { get; } = new();

    public int UnreachableLeft { get; set; }

    /// <summary>Los próximos GET de la sesión fallan sin respuesta (sin conexión al consultar).</summary>
    public int UnreachablePollsLeft { get; set; }

    public int LostResponsesLeft { get; set; }

    public HttpStatusCode? NextStatus { get; set; }

    public bool Identified { get; private set; }

    public int Opened => _sessions.Count;

    public int SessionReads => Paths.Count(p => p.StartsWith("GET ", StringComparison.Ordinal) && p.Contains("/sessions/", StringComparison.Ordinal));

    public string? StatusOf(Guid sessionId) => _sessions.SingleOrDefault(s => s.Id == sessionId)?.Status;

    public MobileApiClient Client() => new(new HttpClient(this) { BaseAddress = new Uri("http://sciph.test/") });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Paths.Add($"{request.Method.Method} {path}");
        var isPoll = request.Method == HttpMethod.Get && path.Contains("/sessions/", StringComparison.Ordinal);
        if (UnreachableLeft > 0 || (isPoll && UnreachablePollsLeft > 0))
        {
            if (isPoll && UnreachablePollsLeft > 0)
            {
                UnreachablePollsLeft--;
            }
            else
            {
                UnreachableLeft--;
            }

            throw new HttpRequestException("Connection refused (test).");
        }

        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        if (body is not null)
        {
            Bodies.Add(body);
        }

        if (NextStatus is { } forced)
        {
            NextStatus = null;
            return FakeMobileBff.Problem(forced, "Forced (test).");
        }

        var segments = path.Trim('/').Split('/');
        var response = segments switch
        {
            ["api", "mobile", "devices", "fingerprint-readers"] => JsonResponse(HttpStatusCode.OK, Readers.Select(Reader)),
            ["api", "mobile", "emergencies", var id, "identify", "fingerprint", "sessions"] => Open(Guid.Parse(id), body!),
            ["api", "mobile", "emergencies", var id, "identify", "fingerprint", "sessions", var sessionId] => Read(Guid.Parse(id), Guid.Parse(sessionId)),
            ["api", "mobile", "emergencies", var id, "identify", "fingerprint", "sessions", var sessionId, "confirm"] => Confirm(Guid.Parse(id), Guid.Parse(sessionId), body!),
            ["api", "mobile", "emergencies", var id, "identify", "fingerprint", "sessions", var sessionId, "cancel"] => Cancel(Guid.Parse(id), Guid.Parse(sessionId)),
            _ => FakeMobileBff.Problem(HttpStatusCode.NotFound, "Unknown path (test).")
        };

        if (request.Method == HttpMethod.Post && LostResponsesLeft > 0)
        {
            LostResponsesLeft--;
            throw new HttpRequestException("Connection reset after the request was processed (test).");
        }

        return response;
    }

    private static object Reader(Guid id) => new
    {
        id,
        code = id == ReaderA ? "ESP32-AMB-01" : "ESP32-AMB-02",
        name = id == ReaderA ? "Ambulancia 1" : "Ambulancia 2",
        lastSeenAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
    };

    private HttpResponseMessage Open(Guid emergencyId, string body)
    {
        var request = JsonDocument.Parse(body).RootElement;
        var key = request.GetProperty("clientRequestId").GetGuid();
        var readerId = request.GetProperty("deviceId").GetGuid();
        var payload = $"{emergencyId}|{readerId}";
        if (_openKeys.TryGetValue(key, out var existing))
        {
            return existing.Payload == payload
                ? JsonResponse(HttpStatusCode.OK, Summary(_sessions.Single(s => s.Id == existing.SessionId)))
                : FakeMobileBff.Problem(HttpStatusCode.Conflict, "Conflict.");
        }

        if (!Readers.Contains(readerId))
        {
            return FakeMobileBff.Problem(HttpStatusCode.NotFound, "Reader not found.");
        }

        if (Identified || _sessions.Any(s => s.EmergencyId == emergencyId && s.Status is "Pending" or "Matched"))
        {
            return FakeMobileBff.Problem(HttpStatusCode.Conflict, "Active session.");
        }

        var session = new Session
        {
            Id = Guid.NewGuid(),
            EmergencyId = emergencyId,
            ReaderId = readerId,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(90)
        };
        _sessions.Add(session);
        _openKeys[key] = (payload, session.Id);
        return JsonResponse(HttpStatusCode.Created, Summary(session));
    }

    private HttpResponseMessage Read(Guid emergencyId, Guid sessionId)
    {
        if (_sessions.SingleOrDefault(s => s.Id == sessionId && s.EmergencyId == emergencyId) is not { } session)
        {
            return FakeMobileBff.Problem(HttpStatusCode.NotFound, "Not found.");
        }

        if (session.Status == "Pending" && Script.TryDequeue(out var next))
        {
            var parts = next.Split(':');
            session.Status = parts[0];
            session.Failure = parts[0] == "Failed" ? parts[1] : null;
            session.WithCandidate = !(parts.Length > 1 && parts[1] == "NoCandidate");
        }

        return JsonResponse(HttpStatusCode.OK, Summary(session));
    }

    private HttpResponseMessage Confirm(Guid emergencyId, Guid sessionId, string body)
    {
        var key = JsonDocument.Parse(body).RootElement.GetProperty("clientRequestId").GetGuid();
        var payload = $"{emergencyId}|{sessionId}";
        if (_confirmKeys.TryGetValue(key, out var existing))
        {
            return existing.Payload == payload ? JsonResponse(HttpStatusCode.OK, Emergency(emergencyId)) : FakeMobileBff.Problem(HttpStatusCode.Conflict, "Conflict.");
        }

        if (_sessions.SingleOrDefault(s => s.Id == sessionId && s.EmergencyId == emergencyId) is not { } session)
        {
            return FakeMobileBff.Problem(HttpStatusCode.NotFound, "Not found.");
        }

        if (session.Status != "Matched" || !session.WithCandidate || Identified)
        {
            return FakeMobileBff.Problem(HttpStatusCode.Conflict, "Not matched.");
        }

        session.Status = "Confirmed";
        Identified = true;
        _confirmKeys[key] = (payload, emergencyId);
        return JsonResponse(HttpStatusCode.OK, Emergency(emergencyId));
    }

    private HttpResponseMessage Cancel(Guid emergencyId, Guid sessionId)
    {
        if (_sessions.SingleOrDefault(s => s.Id == sessionId && s.EmergencyId == emergencyId) is not { } session)
        {
            return FakeMobileBff.Problem(HttpStatusCode.NotFound, "Not found.");
        }

        if (session.Status == "Confirmed")
        {
            return FakeMobileBff.Problem(HttpStatusCode.Conflict, "Confirmed.");
        }

        if (session.Status is "Pending" or "Matched")
        {
            session.Status = "Cancelled";
        }

        return JsonResponse(HttpStatusCode.OK, Summary(session));
    }

    private object Emergency(Guid id) => new
    {
        id,
        temporaryPatientCode = "NN-TEST",
        status = "Created",
        startedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
        chiefComplaint = "Inconsciente",
        patient = new { id = PatientId, displayName = "Ana Pérez" },
        activeTransfer = (object?)null
    };

    private static object Summary(Session session) => new
    {
        id = session.Id,
        status = session.Status,
        readerId = session.ReaderId,
        expiresAtUtc = session.ExpiresAtUtc,
        failure = session.Failure,
        candidate = session.Status == "Matched" && session.WithCandidate ? new { displayName = "Ana Pérez", dateOfBirth = "1985-03-14" } : null
    };

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json")
    };
}
