using System.Net;
using System.Text;
using System.Text.Json;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Tests.Auth;

namespace SCI_PH.Mobile.Tests.Paramedic;

/// <summary>
/// POST api/mobile/emergencies/{id}/notes en memoria con el comportamiento del servidor real: 201 la primera vez, 200 si
/// se repite la clave con la misma observación, 409 si la clave llega con otros datos. Puede fallar sin respuesta (antes
/// o después de registrar) y forzar un estado (400, 403, 404, 409…).
/// </summary>
internal sealed class FakeNotesBff : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<Guid, (string Payload, Guid Id)> _byKey = [];

    public List<string> Bodies { get; } = [];

    public List<string> Paths { get; } = [];

    public List<HttpStatusCode> Answers { get; } = [];

    public int UnreachableLeft { get; set; }

    public int LostResponsesLeft { get; set; }

    public HttpStatusCode? NextStatus { get; set; }

    /// <summary>Observaciones registradas (una por clave).</summary>
    public int Recorded => _byKey.Count;

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

        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        Bodies.Add(body);
        if (NextStatus is { } forced)
        {
            NextStatus = null;
            Answers.Add(forced);
            return FakeMobileBff.Problem(forced, "Forced (test).");
        }

        var emergencyId = Guid.Parse(path.Split('/')[4]);
        var json = JsonDocument.Parse(body).RootElement;
        var key = json.GetProperty("clientRequestId").GetGuid();
        var payload = emergencyId + "|" + string.Join("|", json.EnumerateObject().Where(p => p.Name != "clientRequestId").Select(p => $"{p.Name}={p.Value}"));

        HttpResponseMessage response;
        if (_byKey.TryGetValue(key, out var existing))
        {
            response = existing.Payload == payload
                ? Respond(HttpStatusCode.OK, existing.Id, emergencyId, json)
                : FakeMobileBff.Problem(HttpStatusCode.Conflict, "Conflict.");
        }
        else
        {
            var id = Guid.NewGuid();
            _byKey[key] = (payload, id);
            response = Respond(HttpStatusCode.Created, id, emergencyId, json);
        }

        Answers.Add(response.StatusCode);
        if (LostResponsesLeft > 0)
        {
            LostResponsesLeft--;
            throw new HttpRequestException("Connection reset after the request was processed (test).");
        }

        return response;
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, Guid id, Guid emergencyId, JsonElement request) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            id,
            encounterId = emergencyId,
            type = "Prehospital",
            content = request.GetProperty("content").GetString(),
            occurredAtUtc = request.GetProperty("occurredAt").GetDateTimeOffset().ToUniversalTime()
        }, Json), Encoding.UTF8, "application/json")
    };
}
