using System.Net;
using System.Text;
using System.Text.Json;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Tests.Auth;

namespace SCI_PH.Mobile.Tests.Paramedic;

/// <summary>
/// POST api/mobile/emergencies/{id}/vital-signs en memoria con el comportamiento del servidor real: 201 la primera vez,
/// 200 si se repite la clave con la misma toma, 409 si la clave llega con otra toma. Puede fallar sin respuesta (antes
/// o después de registrar) y forzar un estado (400, 403…).
/// </summary>
internal sealed class FakeVitalSignsBff : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<Guid, string> _byKey = [];

    public List<string> Bodies { get; } = [];

    public List<string> Paths { get; } = [];

    public List<HttpStatusCode> Answers { get; } = [];

    public int UnreachableLeft { get; set; }

    public int LostResponsesLeft { get; set; }

    public HttpStatusCode? NextStatus { get; set; }

    /// <summary>Tomas registradas (una por clave).</summary>
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
            response = existing == payload ? Respond(HttpStatusCode.OK, emergencyId, json) : FakeMobileBff.Problem(HttpStatusCode.Conflict, "Conflict.");
        }
        else
        {
            _byKey[key] = payload;
            response = Respond(HttpStatusCode.Created, emergencyId, json);
        }

        Answers.Add(response.StatusCode);
        if (LostResponsesLeft > 0)
        {
            LostResponsesLeft--;
            throw new HttpRequestException("Connection reset after the request was processed (test).");
        }

        return response;
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, Guid emergencyId, JsonElement request) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            encounterId = emergencyId,
            recordedAtUtc = request.GetProperty("recordedAt").GetDateTimeOffset().ToUniversalTime(),
            measurements = Array.Empty<object>()
        }, Json), Encoding.UTF8, "application/json")
    };
}
