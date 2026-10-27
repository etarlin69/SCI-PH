using System.Net;
using System.Text;
using System.Text.Json;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Tests.Auth;

namespace SCI_PH.Mobile.Tests.Paramedic;

/// <summary>
/// GET api/mobile/catalogs/medications y POST api/mobile/emergencies/{id}/medication-administrations en memoria con el
/// comportamiento del servidor real: catálogo de activos; 201 la primera vez, 200 si se repite la clave con la misma
/// dosis, 409 si la clave llega con otros datos. Puede fallar sin respuesta (antes o después de registrar) y forzar un
/// estado (400, 403, 404, 409…).
/// </summary>
internal sealed class FakeMedicationsBff : HttpMessageHandler
{
    public static readonly Guid Paracetamol = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Metamizol = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid SterileWater = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<Guid, (string Payload, Guid Id)> _byKey = [];

    public List<string> Bodies { get; } = [];

    public List<string> Paths { get; } = [];

    public List<HttpStatusCode> Answers { get; } = [];

    public int UnreachableLeft { get; set; }

    public int LostResponsesLeft { get; set; }

    public HttpStatusCode? NextStatus { get; set; }

    /// <summary>Dosis registradas (una por clave).</summary>
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

        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        if (body is not null)
        {
            Bodies.Add(body);
        }

        if (NextStatus is { } forced)
        {
            NextStatus = null;
            Answers.Add(forced);
            return FakeMobileBff.Problem(forced, "Forced (test).");
        }

        if (body is null)
        {
            // Only active medications, as the server lists them (by name, then concentration).
            return JsonResponse(HttpStatusCode.OK, new object[]
            {
                new { id = SterileWater, code = (string?)null, name = "Agua estéril", presentation = (string?)null, concentration = (string?)null, defaultRoute = (string?)null },
                new { id = Metamizol, code = (string?)"DEV-METAMIZOL-1G", name = "Metamizol", presentation = (string?)"Solución inyectable", concentration = (string?)"1 g/2 mL", defaultRoute = (string?)"Intravenous" },
                new { id = Paracetamol, code = (string?)"DEV-PARACETAMOL-500", name = "Paracetamol", presentation = (string?)"Tableta", concentration = (string?)"500 mg", defaultRoute = (string?)"Oral" }
            });
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

    private static HttpResponseMessage Respond(HttpStatusCode status, Guid id, Guid emergencyId, JsonElement request) =>
        JsonResponse(status, new
        {
            id,
            encounterId = emergencyId,
            medicationId = request.GetProperty("medicationId").GetGuid(),
            medicationName = "Medicamento",
            dose = request.GetProperty("dose").GetDecimal(),
            doseUnit = request.GetProperty("doseUnit").GetString(),
            route = request.GetProperty("route").GetString(),
            administeredAtUtc = request.GetProperty("administeredAt").GetDateTimeOffset().ToUniversalTime(),
            notes = request.TryGetProperty("notes", out var notes) ? notes.GetString() : null
        });

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json")
    };
}
