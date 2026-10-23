using System.Net;
using System.Text;
using System.Text.Json;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Tests.Auth;

namespace SCI_PH.Mobile.Tests.Paramedic;

/// <summary>
/// api/mobile/emergencies en memoria con el comportamiento del servidor real: 201 la primera vez, 200 con la misma
/// emergencia si se repite la clave con los mismos datos, 409 si la clave llega con otros datos. Puede fallar sin
/// respuesta (antes o después de crear) y forzar un estado de error.
/// </summary>
internal sealed class FakeEmergenciesBff : HttpMessageHandler
{
    public const string TemporaryPatientCode = "NN-0123456789ABCDEF0123456789ABCDEF";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<Guid, (string Payload, Guid Id)> _byKey = [];

    public List<string> Bodies { get; } = [];

    public List<string> Paths { get; } = [];

    /// <summary>Las próximas peticiones fallan sin llegar al servidor.</summary>
    public int UnreachableLeft { get; set; }

    /// <summary>Las próximas peticiones se procesan, pero la respuesta se pierde.</summary>
    public int LostResponsesLeft { get; set; }

    /// <summary>Estado forzado (por ejemplo 400 o 403) para la próxima petición.</summary>
    public HttpStatusCode? NextStatus { get; set; }

    public int Created => _byKey.Count;

    public MobileApiClient Client() => new(new HttpClient(this) { BaseAddress = new Uri("http://sciph.test/") });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Paths.Add($"{request.Method.Method} {path}");
        if (UnreachableLeft > 0)
        {
            UnreachableLeft--;
            throw new HttpRequestException("Connection refused (test).");
        }

        if (NextStatus is { } forced)
        {
            NextStatus = null;
            return FakeMobileBff.Problem(forced, "Forced (test).");
        }

        if (request.Method == HttpMethod.Get)
        {
            var id = Guid.Parse(path[(path.LastIndexOf('/') + 1)..]);
            return _byKey.Values.Any(v => v.Id == id) ? Respond(HttpStatusCode.OK, id) : FakeMobileBff.Problem(HttpStatusCode.NotFound, "Not found.");
        }

        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        Bodies.Add(body);
        var json = JsonDocument.Parse(body).RootElement;
        var key = json.GetProperty("clientRequestId").GetGuid();
        var payload = json.GetProperty("startedAt").GetString() + "|" + json.GetProperty("chiefComplaint").GetString();

        HttpResponseMessage response;
        if (_byKey.TryGetValue(key, out var existing))
        {
            response = existing.Payload == payload
                ? Respond(HttpStatusCode.OK, existing.Id)
                : FakeMobileBff.Problem(HttpStatusCode.Conflict, "Conflict.");
        }
        else
        {
            var id = Guid.NewGuid();
            _byKey[key] = (payload, id);
            response = Respond(HttpStatusCode.Created, id);
        }

        if (LostResponsesLeft > 0)
        {
            LostResponsesLeft--;
            throw new HttpRequestException("Connection reset after the request was processed (test).");
        }

        return response;
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, Guid id) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            id,
            temporaryPatientCode = TemporaryPatientCode,
            status = "Created",
            startedAtUtc = new DateTimeOffset(2026, 10, 23, 14, 0, 0, TimeSpan.Zero),
            chiefComplaint = "Dolor torácico",
            patient = (object?)null
        }, Json), Encoding.UTF8, "application/json")
    };
}
