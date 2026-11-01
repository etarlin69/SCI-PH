using System.Net;
using System.Text;
using SCI_PH.Mobile.Common.Api;

namespace SCI_PH.Mobile.Tests.Patient;

/// <summary>
/// api/mobile/patient/me en memoria con respuestas en el formato real del Mobile BFF (enums como texto). Registra cada
/// ruta pedida para comprobar que la app nunca envía un id de paciente. Puede forzar un estado o fallar sin respuesta.
/// </summary>
internal sealed class FakePatientSelfBff : HttpMessageHandler
{
    public const string Me = "/api/mobile/patient/me";

    public Dictionary<string, string> Responses { get; } = new()
    {
        [Me] = """
            {"givenNames":"Ana","familyNames":"Pérez","dateOfBirth":"1990-05-20","sex":"Female","identityStatus":"Verified",
             "document":{"type":"NationalId","value":"***5678"},"email":"ana@sci-ph.test","phone":null}
            """,
        [$"{Me}/summary"] = """
            {"displayName":"Ana Pérez","encounterCount":3,"resultCount":2,
             "latestEncounter":{"id":"0b1f0000-0000-0000-0000-000000000001","type":"Consultation","status":"Closed","reason":"Fiebre",
                                "startedAtUtc":"2026-10-20T15:00:00Z","endedAtUtc":null,"clinicName":"Hospital Central"},
             "latestResult":{"id":"0b1f0000-0000-0000-0000-000000000002","encounterId":"0b1f0000-0000-0000-0000-000000000001",
                             "type":"Laboratory","status":"Final","name":"Hemoglobina","codeSystem":null,"code":null,"resultText":null,
                             "numericValue":13.5,"unit":"g/dL","referenceRange":"12-16","resultedAtUtc":"2026-10-20T16:00:00Z"},
             "activeAllergies":[{"id":"0b1f0000-0000-0000-0000-000000000003","substance":"Penicilina","severity":"Severe",
                                 "status":"Active","reaction":"Urticaria","onsetDate":null}]}
            """,
        [$"{Me}/encounters"] = """
            [{"id":"0b1f0000-0000-0000-0000-000000000001","type":"Prehospital","status":"Received","reason":null,
              "startedAtUtc":"2026-10-20T15:00:00Z","endedAtUtc":null,"clinicName":null}]
            """,
        [$"{Me}/allergies"] = """
            [{"id":"0b1f0000-0000-0000-0000-000000000003","substance":"Penicilina","severity":"Severe","status":"Active","reaction":"Urticaria","onsetDate":null},
             {"id":"0b1f0000-0000-0000-0000-000000000004","substance":"Látex","severity":"Mild","status":"Resolved","reaction":null,"onsetDate":null}]
            """,
        [$"{Me}/diagnoses"] = """
            [{"id":"0b1f0000-0000-0000-0000-000000000005","encounterId":"0b1f0000-0000-0000-0000-000000000001","description":"Faringitis aguda",
              "codeSystem":null,"code":null,"type":"Definitive","isPrimary":true,"recordedAtUtc":"2026-10-20T15:30:00Z","confirmedAtUtc":null}]
            """,
        [$"{Me}/prescriptions"] = """
            [{"id":"0b1f0000-0000-0000-0000-000000000006","encounterId":"0b1f0000-0000-0000-0000-000000000001","status":"Cancelled","notes":null,
              "issuedAtUtc":"2026-10-20T15:40:00Z","validUntilUtc":null,"cancelledAtUtc":"2026-10-20T18:00:00Z",
              "items":[{"medicationName":"Amoxicilina","presentation":"Cápsula","concentration":"500 mg","dose":500,"doseUnit":"mg",
                        "route":"Oral","frequency":"Cada 8 horas","durationDays":7,"instructions":null}]}]
            """,
        [$"{Me}/results"] = """
            [{"id":"0b1f0000-0000-0000-0000-000000000002","encounterId":"0b1f0000-0000-0000-0000-000000000001","type":"Laboratory","status":"Corrected",
              "name":"Hemoglobina","codeSystem":null,"code":null,"resultText":null,"numericValue":13.5,"unit":"g/dL","referenceRange":"12-16",
              "resultedAtUtc":"2026-10-20T16:00:00Z"},
             {"id":"0b1f0000-0000-0000-0000-000000000007","encounterId":"0b1f0000-0000-0000-0000-000000000001","type":"NewTestKind","status":"Final",
              "name":"Radiografía de tórax","codeSystem":null,"code":null,"resultText":"Sin hallazgos","numericValue":null,"unit":null,
              "referenceRange":null,"resultedAtUtc":null}]
            """
    };

    public List<string> Paths { get; } = [];

    /// <summary>Estado forzado para todas las peticiones (por ejemplo 403 o 404).</summary>
    public HttpStatusCode? Status { get; set; }

    /// <summary>Las peticiones fallan sin respuesta, como sin conexión.</summary>
    public bool Unreachable { get; set; }

    public MobileApiClient Client() => new(new HttpClient(this) { BaseAddress = new Uri("http://sciph.test/") });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Paths)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
        }

        if (Unreachable)
        {
            throw new HttpRequestException("unreachable");
        }

        if (Status is { } status)
        {
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("""{"title":"forced"}""", Encoding.UTF8, "application/problem+json")
            });
        }

        return Task.FromResult(Responses.TryGetValue(request.RequestUri!.AbsolutePath, out var json)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
