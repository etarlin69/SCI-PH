using System.Net;
using System.Text;
using System.Text.Json;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Tests.Auth;

namespace SCI_PH.Mobile.Tests.Paramedic;

/// <summary>
/// POST api/mobile/emergencies/{id}/identify/document/resolve en memoria: el candidato (nombre, nacimiento, documento
/// enmascarado) sin guardar nada; 404 si ningún paciente tiene el documento; 409 si la emergencia ya está identificada.
/// POST api/mobile/emergencies/{id}/identify/document (la confirmación) con el comportamiento del servidor real: 200 con la
/// MISMA emergencia identificada; 200 de nuevo si se repite la clave con los mismos datos; 409 si la clave llega con otros
/// datos o si la emergencia ya fue identificada por otra solicitud; 404 si ningún paciente tiene el documento (y no
/// guarda nada). Puede fallar sin respuesta (antes o después de identificar).
/// </summary>
internal sealed class FakeDocumentIdentificationBff : HttpMessageHandler
{
    public const string TemporaryPatientCode = "NN-0123456789ABCDEF0123456789ABCDEF";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<Guid, (string Payload, Guid EmergencyId)> _byKey = [];
    private readonly Dictionary<Guid, (Guid Id, string DisplayName)> _identified = [];

    /// <summary>"Tipo|número" → paciente. Coincidencia exacta (la normalización es del servidor real).</summary>
    public Dictionary<string, (Guid Id, string DisplayName)> Patients { get; } = [];

    public List<string> Bodies { get; } = [];

    public List<string> Paths { get; } = [];

    public int UnreachableLeft { get; set; }

    public int ResolveCalls { get; private set; }

    public int LostResponsesLeft { get; set; }

    public MobileApiClient Client() => new(new HttpClient(this) { BaseAddress = new Uri("http://sciph.test/") });

    public (Guid Id, string DisplayName)? IdentifiedPatient(Guid emergencyId) =>
        _identified.TryGetValue(emergencyId, out var patient) ? patient : null;

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
        var emergencyId = Guid.Parse(path.Split('/')[4]);
        var json = JsonDocument.Parse(body).RootElement;
        var document = json.GetProperty("documentType").GetString() + "|" + json.GetProperty("documentNumber").GetString();

        // Resolve: the candidate only; nothing is stored.
        if (path.EndsWith("/resolve", StringComparison.Ordinal))
        {
            ResolveCalls++;
            if (_identified.ContainsKey(emergencyId))
            {
                return FakeMobileBff.Problem(HttpStatusCode.Conflict, "Conflict.");
            }

            return Patients.TryGetValue(document, out var candidate)
                ? Candidate(candidate.DisplayName, json.GetProperty("documentType").GetString()!)
                : FakeMobileBff.Problem(HttpStatusCode.NotFound, "Not found.");
        }

        var key = json.GetProperty("clientRequestId").GetGuid();
        var payload = emergencyId + "|" + document;

        HttpResponseMessage response;
        if (_byKey.TryGetValue(key, out var existing))
        {
            response = existing.Payload == payload
                ? Respond(existing.EmergencyId, _identified[existing.EmergencyId])
                : FakeMobileBff.Problem(HttpStatusCode.Conflict, "Conflict.");
        }
        else if (!Patients.TryGetValue(document, out var patient))
        {
            response = FakeMobileBff.Problem(HttpStatusCode.NotFound, "Not found.");
        }
        else if (_identified.ContainsKey(emergencyId))
        {
            response = FakeMobileBff.Problem(HttpStatusCode.Conflict, "Conflict.");
        }
        else
        {
            _identified[emergencyId] = patient;
            _byKey[key] = (payload, emergencyId);
            response = Respond(emergencyId, patient);
        }

        if (LostResponsesLeft > 0)
        {
            LostResponsesLeft--;
            throw new HttpRequestException("Connection reset after the request was processed (test).");
        }

        return response;
    }

    private static HttpResponseMessage Candidate(string displayName, string documentType) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            displayName,
            dateOfBirth = "1985-03-14",
            document = new { type = documentType, value = "*******5678" }
        }, Json), Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Respond(Guid emergencyId, (Guid Id, string DisplayName) patient) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            id = emergencyId,
            temporaryPatientCode = TemporaryPatientCode,
            status = "Created",
            startedAtUtc = new DateTimeOffset(2026, 10, 23, 14, 0, 0, TimeSpan.Zero),
            chiefComplaint = "Dolor torácico",
            patient = new
            {
                id = patient.Id,
                displayName = patient.DisplayName,
                birthDate = "1985-03-14",
                biologicalSex = "Female",
                identityStatus = "Unverified",
                primaryIdentifier = new { type = "NationalId", value = "*******5678" }
            }
        }, Json), Encoding.UTF8, "application/json")
    };
}
