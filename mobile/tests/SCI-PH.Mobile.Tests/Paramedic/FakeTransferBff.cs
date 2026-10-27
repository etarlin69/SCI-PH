using System.Net;
using System.Text;
using System.Text.Json;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Tests.Auth;

namespace SCI_PH.Mobile.Tests.Paramedic;

/// <summary>
/// El traslado del Mobile BFF en memoria, con el comportamiento del servidor real: GET de la emergencia con su
/// activeTransfer, destinos activos, solicitud idempotente por clave (201/200/409, un solo traslado activo) e inicio y
/// cancelación sobre un traslado concreto, idempotentes por estado (200; 409 si el estado no lo permite; 404 si no es de
/// la emergencia). Puede fallar sin respuesta (antes o después de procesar) y forzar un estado.
/// </summary>
internal sealed class FakeTransferBff : HttpMessageHandler
{
    public static readonly Guid Central = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid Norte = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<Guid, (string Payload, Guid TransferId)> _byKey = [];

    private sealed class Transfer
    {
        public Guid Id { get; init; }
        public Guid EmergencyId { get; init; }
        public Guid DestinationId { get; init; }
        public string Status { get; set; } = "Requested";
        public DateTimeOffset RequestedAtUtc { get; init; }
        public DateTimeOffset? SentAtUtc { get; set; }
        public DateTimeOffset? CancelledAtUtc { get; set; }
    }

    private readonly List<Transfer> _transfers = [];

    public List<string> Paths { get; } = [];

    public List<string> Bodies { get; } = [];

    public List<HttpStatusCode> Answers { get; } = [];

    public int UnreachableLeft { get; set; }

    public int LostResponsesLeft { get; set; }

    public HttpStatusCode? NextStatus { get; set; }

    /// <summary>Traslados creados (uno por clave).</summary>
    public int Requested => _transfers.Count;

    public string? StatusOf(Guid transferId) => _transfers.SingleOrDefault(t => t.Id == transferId)?.Status;

    /// <summary>Simula que el servidor ya tiene un traslado (por ejemplo, tras reabrir la app).</summary>
    public Guid Seed(Guid emergencyId, string status)
    {
        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            EmergencyId = emergencyId,
            DestinationId = Central,
            Status = status,
            RequestedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            SentAtUtc = status == "InTransit" ? DateTimeOffset.UtcNow.AddMinutes(-2) : null
        };
        _transfers.Add(transfer);
        return transfer.Id;
    }

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

        var segments = path.Trim('/').Split('/');
        var response = segments switch
        {
            ["api", "mobile", "catalogs", "transfer-destinations"] => JsonResponse(HttpStatusCode.OK, new[] { Destination(Central), Destination(Norte) }),
            ["api", "mobile", "emergencies", var id] => Emergency(Guid.Parse(id)),
            ["api", "mobile", "emergencies", var id, "transfers"] => RequestTransfer(Guid.Parse(id), body!),
            ["api", "mobile", "emergencies", var id, "transfers", var transferId, var step] => Step(Guid.Parse(id), Guid.Parse(transferId), step),
            _ => FakeMobileBff.Problem(HttpStatusCode.NotFound, "Unknown path (test).")
        };

        Answers.Add(response.StatusCode);
        if (request.Method == HttpMethod.Post && LostResponsesLeft > 0)
        {
            LostResponsesLeft--;
            throw new HttpRequestException("Connection reset after the request was processed (test).");
        }

        return response;
    }

    private HttpResponseMessage Emergency(Guid id)
    {
        var active = _transfers.SingleOrDefault(t => t.EmergencyId == id && t.Status is "Requested" or "InTransit");
        return JsonResponse(HttpStatusCode.OK, new
        {
            id,
            temporaryPatientCode = "NN-TEST",
            status = active?.Status == "InTransit" ? "Transferred" : "InProgress",
            startedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30),
            chiefComplaint = "Politrauma",
            patient = (object?)null,
            activeTransfer = active is null ? null : Summary(active)
        });
    }

    private HttpResponseMessage RequestTransfer(Guid emergencyId, string body)
    {
        var json = JsonDocument.Parse(body).RootElement;
        var key = json.GetProperty("clientRequestId").GetGuid();
        var payload = emergencyId + "|" + string.Join("|", json.EnumerateObject().Where(p => p.Name != "clientRequestId").Select(p => $"{p.Name}={p.Value}"));
        if (_byKey.TryGetValue(key, out var existing))
        {
            return existing.Payload == payload
                ? JsonResponse(HttpStatusCode.OK, Summary(_transfers.Single(t => t.Id == existing.TransferId)))
                : FakeMobileBff.Problem(HttpStatusCode.Conflict, "Conflict.");
        }

        if (_transfers.Any(t => t.EmergencyId == emergencyId && t.Status is "Requested" or "InTransit"))
        {
            return FakeMobileBff.Problem(HttpStatusCode.Conflict, "Active transfer.");
        }

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            EmergencyId = emergencyId,
            DestinationId = json.GetProperty("destinationClinicId").GetGuid(),
            RequestedAtUtc = DateTimeOffset.UtcNow
        };
        _transfers.Add(transfer);
        _byKey[key] = (payload, transfer.Id);
        return JsonResponse(HttpStatusCode.Created, Summary(transfer));
    }

    private HttpResponseMessage Step(Guid emergencyId, Guid transferId, string step)
    {
        var transfer = _transfers.SingleOrDefault(t => t.Id == transferId && t.EmergencyId == emergencyId);
        if (transfer is null)
        {
            return FakeMobileBff.Problem(HttpStatusCode.NotFound, "Transfer not found.");
        }

        switch (step, transfer.Status)
        {
            case ("start", "Requested"):
                transfer.Status = "InTransit";
                transfer.SentAtUtc = DateTimeOffset.UtcNow;
                break;
            case ("start", "InTransit"):
            case ("cancel", "Cancelled"):
                break;
            case ("cancel", "Requested" or "InTransit"):
                transfer.Status = "Cancelled";
                transfer.CancelledAtUtc = DateTimeOffset.UtcNow;
                break;
            default:
                return FakeMobileBff.Problem(HttpStatusCode.Conflict, "Invalid transfer state.");
        }

        return JsonResponse(HttpStatusCode.OK, Summary(transfer));
    }

    private static object Destination(Guid id) => id == Central
        ? new { id, name = "Hospital Central", type = "Hospital", municipality = "Distrito Nacional", province = "Santo Domingo" }
        : new { id, name = "Centro Norte", type = "EmergencyCenter", municipality = "Santiago", province = "Santiago" };

    private static object Summary(Transfer t) => new
    {
        id = t.Id,
        encounterId = t.EmergencyId,
        status = t.Status,
        destination = Destination(t.DestinationId),
        requestedAtUtc = t.RequestedAtUtc,
        sentAtUtc = t.SentAtUtc,
        cancelledAtUtc = t.CancelledAtUtc
    };

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json")
    };
}
