using System.Net;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Features.Patient;
using SCI_PH.Mobile.Features.Patient.Services;
using SCI_PH.Mobile.Features.Patient.ViewModels;

namespace SCI_PH.Mobile.Tests.Patient;

// Historial -> atención -> detalle, con HttpPatientDataService real contra FakePatientSelfBff: solo viaja el id de la
// atención (el servidor decide si es del paciente), la de otra persona o una inexistente es "no disponible", las
// secciones vacías son un estado válido, y el detalle se vacía al cambiar de cuenta como las demás pantallas.
public class PatientEncounterDetailTests
{
    private static readonly Guid EncounterId = Guid.Parse("0b1f0000-0000-0000-0000-000000000001");
    private static readonly string DetailPath = $"{FakePatientSelfBff.Me}/encounters/{EncounterId:D}";

    private readonly FakePatientSelfBff _bff = new();
    private readonly ClinicalSessionService _session = PatientSession.SignedIn();

    public PatientEncounterDetailTests()
    {
        _bff.Responses[DetailPath] = """
            {"encounter":{"id":"0b1f0000-0000-0000-0000-000000000001","type":"Emergency","status":"Completed",
                          "startedAtUtc":"2026-10-20T15:00:00Z","endedAtUtc":"2026-10-22T15:00:00Z","clinicName":"Hospital Central"},
             "diagnoses":[{"id":"0b1f0000-0000-0000-0000-000000000005","encounterId":"0b1f0000-0000-0000-0000-000000000001",
                           "description":"Neumonía adquirida en la comunidad","type":"Definitive","recordedAtUtc":"2026-10-21T15:30:00Z"},
                          {"id":"0b1f0000-0000-0000-0000-000000000008","encounterId":"0b1f0000-0000-0000-0000-000000000001",
                           "description":"Hipertensión arterial","type":"Secondary","recordedAtUtc":"2026-10-21T15:35:00Z"}],
             "results":[{"id":"0b1f0000-0000-0000-0000-000000000002","encounterId":"0b1f0000-0000-0000-0000-000000000001",
                         "type":"Imaging","status":"Final","name":"Radiografía de tórax","resultText":"Infiltrado basal derecho",
                         "numericValue":null,"unit":null,"referenceRange":null,"resultedAtUtc":"2026-10-21T10:00:00Z"},
                        {"id":"0b1f0000-0000-0000-0000-000000000009","encounterId":"0b1f0000-0000-0000-0000-000000000001",
                         "type":"Laboratory","status":"Final","name":"Leucocitos","resultText":null,
                         "numericValue":14.2,"unit":"x10^3/µL","referenceRange":"4-11","resultedAtUtc":"2026-10-21T09:00:00Z"}],
             "prescriptions":[{"id":"0b1f0000-0000-0000-0000-000000000006","encounterId":"0b1f0000-0000-0000-0000-000000000001","status":"Issued",
                               "issuedAtUtc":"2026-10-22T14:00:00Z","validUntilUtc":null,"cancelledAtUtc":null,
                               "items":[{"medicationName":"Amoxicilina","presentation":"Cápsula","concentration":"500 mg","dose":500,"doseUnit":"mg",
                                         "route":"Oral","frequency":"Cada 8 horas","durationDays":7,"instructions":null}]}]}
            """;
    }

    private HttpPatientDataService Data() => new(_bff.Client());

    private EncounterDetailViewModel Detail(Guid? id = null) => new(Data(), _session) { EncounterId = id ?? EncounterId };

    [Fact]
    public async Task EachEncounterOfTheHistoryOpensItsDetailWithOnlyItsId()
    {
        var navigator = new FakeNavigator();
        var history = new HistoryViewModel(Data(), _session, navigator);
        await history.LoadCommand.ExecuteAsync(null);

        var episode = Assert.Single(history.History!.Episodes);
        await history.OpenEncounterCommand.ExecuteAsync(episode);

        Assert.Equal(EncounterId, episode.Id);
        var (route, parameters) = Assert.Single(navigator.Navigations);
        Assert.Equal(PatientRoutes.EncounterDetail, route);
        Assert.Equal(EncounterId, Assert.Single(parameters!, p => p.Key == PatientRoutes.EncounterIdParameter).Value);
        Assert.Single(parameters!);
    }

    [Fact]
    public async Task TheDetailShowsTheEncounterAndWhatWasPublishedInIt()
    {
        var vm = Detail();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasError);
        var detail = vm.Detail!;
        Assert.Equal(("Emergencia", "Hospital Central", "Finalizada"), (detail.Episode.Title, detail.Episode.Facility, detail.Episode.Status));
        Assert.Equal(new DateOnly(2026, 10, 22), detail.EndedOn);
        Assert.Contains("–", detail.PeriodLabel);
        Assert.Equal(["Neumonía adquirida en la comunidad", "Hipertensión arterial"], detail.Diagnoses.Select(d => d.Title));
        Assert.StartsWith("Definitivo · ", detail.Diagnoses[0].Detail);
        Assert.Equal(
            [("Radiografía de tórax", "Infiltrado basal derecho", "Imagen"), ("Leucocitos", "14,2 x10^3/µL", "Laboratorio")],
            detail.Results.Select(r => (r.Name, r.Value, r.Source)));
        Assert.Equal("Rango de referencia: 4-11", detail.Results[1].Note);
        var prescription = Assert.Single(detail.Prescriptions);
        Assert.Equal(("Amoxicilina 500 mg", "500 mg · Oral · Cada 8 horas · 7 días"), (prescription.Title, prescription.Detail));
        Assert.Equal([DetailPath], _bff.Paths);
    }

    [Fact]
    public async Task AnOpenEncounterShowsOnlyItsStartDate()
    {
        _bff.Responses[DetailPath] = """
            {"encounter":{"id":"0b1f0000-0000-0000-0000-000000000001","type":"Prehospital","status":"InProgress",
                          "startedAtUtc":"2026-10-20T15:00:00Z","endedAtUtc":null,"clinicName":null},
             "diagnoses":[],"results":[],"prescriptions":[]}
            """;
        var vm = Detail();

        await vm.LoadCommand.ExecuteAsync(null);

        var detail = vm.Detail!;
        Assert.False(vm.HasError);
        Assert.Equal(("Atención prehospitalaria", "Ambulancia", "En curso"), (detail.Episode.Title, detail.Episode.Facility, detail.Episode.Status));
        Assert.Null(detail.EndedOn);
        Assert.Equal(detail.Episode.DateLabel, detail.PeriodLabel);
        // Secciones vacías: un estado válido, no un error.
        Assert.Empty(detail.Diagnoses);
        Assert.Empty(detail.Results);
        Assert.Empty(detail.Prescriptions);
    }

    // Otra persona o inexistente: el servidor responde el mismo 404 y la pantalla no muestra nada.
    [Theory]
    [InlineData(HttpStatusCode.NotFound, PatientMessages.NotAvailable)]
    [InlineData(HttpStatusCode.Forbidden, PatientMessages.NoRecordAccess)]
    [InlineData(HttpStatusCode.Unauthorized, PatientMessages.SessionEnded)]
    public async Task AnEncounterThatIsNotAvailableShowsItsMessageAndNoData(HttpStatusCode status, string message)
    {
        _bff.Status = status;
        var vm = Detail(Guid.NewGuid());

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(message, vm.ErrorMessage);
        Assert.Null(vm.Detail);
    }

    [Fact]
    public async Task WithoutAnEncounterIdNothingIsRequested()
    {
        var vm = new EncounterDetailViewModel(Data(), _session);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(PatientMessages.NotAvailable, vm.ErrorMessage);
        Assert.Null(vm.Detail);
        Assert.Empty(_bff.Paths);
    }

    [Fact]
    public async Task ClosingOrChangingTheSessionEmptiesTheDetail()
    {
        var vm = Detail();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Detail);

        PatientSession.SignIn(_session, Guid.NewGuid());
        Assert.Null(vm.Detail);

        await vm.LoadCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Detail);
        _session.Clear();
        Assert.Null(vm.Detail);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task ALateDetailOfTheFormerAccountIsDiscarded()
    {
        var vm = Detail();
        _bff.Gate = new TaskCompletionSource();

        var loading = vm.LoadCommand.ExecuteAsync(null);
        PatientSession.SignIn(_session, Guid.NewGuid());
        _bff.Gate.SetResult();
        await loading;

        Assert.Null(vm.Detail);
        Assert.False(vm.HasError);
    }
}
