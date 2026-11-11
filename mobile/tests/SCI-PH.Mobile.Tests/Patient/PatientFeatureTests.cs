using System.Net;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Features.Patient;
using SCI_PH.Mobile.Features.Patient.Services;
using SCI_PH.Mobile.Features.Patient.ViewModels;

namespace SCI_PH.Mobile.Tests.Patient;

// El área del paciente con HttpPatientDataService real contra FakePatientSelfBff: solo rutas /patient/me (nunca un id
// de paciente), traducción de las respuestas a las pantallas, y estados de error sin datos simulados de respaldo.
public class PatientFeatureTests
{
    private readonly FakePatientSelfBff _bff = new();
    private readonly ClinicalSessionService _session = PatientSession.SignedIn();

    private HttpPatientDataService Data() => new(_bff.Client());

    [Fact]
    public async Task EveryRequestIsAnOwnRecordRouteWithoutAPatientId()
    {
        var data = Data();

        await data.GetSummaryAsync();
        await data.GetHistoryAsync();
        await data.GetResultsAsync();
        await data.GetProfileAsync();

        Assert.Equal(
            [FakePatientSelfBff.Me, $"{FakePatientSelfBff.Me}/allergies", $"{FakePatientSelfBff.Me}/diagnoses",
             $"{FakePatientSelfBff.Me}/encounters", $"{FakePatientSelfBff.Me}/prescriptions", $"{FakePatientSelfBff.Me}/results",
             $"{FakePatientSelfBff.Me}/summary"],
            _bff.Paths.Distinct().Order());
        Assert.All(_bff.Paths, path => Assert.DoesNotContain("?", path));
    }

    [Fact]
    public async Task HomeShowsTheRealSummary()
    {
        var vm = new HomeViewModel(Data(), _session);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasError);
        var summary = vm.Summary!;
        Assert.Equal(("Ana Pérez", 2, 3), (summary.DisplayName, summary.AvailableResults, summary.Encounters));
        Assert.Equal(("Consulta", "Hospital Central", "Finalizada"),
            (summary.LatestEpisode!.Title, summary.LatestEpisode.Facility, summary.LatestEpisode.Status));
        Assert.Equal(("Hemoglobina", "13,5 g/dL", "Publicado"), (summary.LatestResult!.Name, summary.LatestResult.Value, summary.LatestResult.Status));
    }

    [Fact]
    public async Task HistoryTranslatesAllergiesDiagnosesPrescriptionsAndEncounters()
    {
        var vm = new HistoryViewModel(Data(), _session, new FakeNavigator());

        await vm.LoadCommand.ExecuteAsync(null);

        var history = vm.History!;
        Assert.Equal(
            [("Penicilina", "Severidad: grave · Urticaria"), ("Látex", "Severidad: leve · Resuelta")],
            history.Allergies.Select(a => (a.Title, a.Detail)));
        Assert.Equal("Faringitis aguda", Assert.Single(history.Diagnoses).Title);
        Assert.StartsWith("Definitivo · ", history.Diagnoses[0].Detail);
        var prescription = Assert.Single(history.Prescriptions);
        Assert.Equal(("Amoxicilina 500 mg", "500 mg · Oral · Cada 8 horas · 7 días · Cancelada"), (prescription.Title, prescription.Detail));
        var episode = Assert.Single(history.Episodes);
        Assert.Equal(("Atención prehospitalaria", "Ambulancia", "En curso"), (episode.Title, episode.Facility, episode.Status));
    }

    [Fact]
    public async Task ResultsShowValueSourceAndRangeAndAnUnknownTypeAsIs()
    {
        var vm = new ResultsViewModel(Data(), _session);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Results.Count);
        var corrected = vm.Results[0];
        Assert.Equal(("Corregido", "13,5 g/dL", "Laboratorio", "Rango de referencia: 12-16"),
            (corrected.Status, corrected.Value, corrected.Source, corrected.Note));
        var text = vm.Results[1];
        Assert.Equal(("Sin hallazgos", "NewTestKind", "Sin fecha"), (text.Value, text.Source, text.DateLabel));
    }

    [Fact]
    public async Task ProfileCombinesTheOwnProfileWithTheSummaryCounts()
    {
        var profile = await Data().GetProfileAsync();

        Assert.Equal(("Ana Pérez", "Femenino", "Cédula ***5678", "ana@sci-ph.test"), (profile.DisplayName, profile.Sex, profile.Document, profile.Email));
        Assert.Null(profile.PhoneNumber);
        Assert.Equal((3, 2, 1), (profile.Encounters, profile.Results, profile.ActiveAllergies));
        Assert.Equal("20 de mayo de 1990", profile.BirthDateLabel);
    }

    [Fact]
    public async Task AnEmptyRecordIsAValidEmptyStateNotAnError()
    {
        _bff.Responses[$"{FakePatientSelfBff.Me}/results"] = "[]";
        foreach (var route in new[] { "allergies", "diagnoses", "prescriptions", "encounters" })
        {
            _bff.Responses[$"{FakePatientSelfBff.Me}/{route}"] = "[]";
        }

        var results = new ResultsViewModel(Data(), _session);
        var history = new HistoryViewModel(Data(), _session, new FakeNavigator());
        await results.LoadCommand.ExecuteAsync(null);
        await history.LoadCommand.ExecuteAsync(null);

        Assert.False(results.HasError);
        Assert.Empty(results.Results);
        Assert.False(history.HasError);
        Assert.Empty(history.History!.Episodes);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, PatientMessages.NoRecordAccess)]
    [InlineData(HttpStatusCode.NotFound, PatientMessages.NotAvailable)]
    [InlineData(HttpStatusCode.Unauthorized, PatientMessages.SessionEnded)]
    [InlineData(HttpStatusCode.InternalServerError, MobileApiErrorMessages.Server)]
    [InlineData(HttpStatusCode.ServiceUnavailable, MobileApiErrorMessages.Server)]
    public async Task AFailureShowsItsMessageAndNoData(HttpStatusCode status, string message)
    {
        _bff.Status = status;
        var home = new HomeViewModel(Data(), _session);
        var results = new ResultsViewModel(Data(), _session);

        await home.LoadCommand.ExecuteAsync(null);
        await results.LoadCommand.ExecuteAsync(null);

        Assert.Equal(message, home.ErrorMessage);
        Assert.Null(home.Summary);
        Assert.Equal(message, results.ErrorMessage);
        Assert.Empty(results.Results);
    }

    [Fact]
    public async Task WithoutConnectionTheScreensShowTheNetworkMessageAndNoFallbackData()
    {
        var home = new HomeViewModel(Data(), _session);
        await home.LoadCommand.ExecuteAsync(null);
        Assert.NotNull(home.Summary);

        _bff.Unreachable = true;
        await home.LoadCommand.ExecuteAsync(null);
        var history = new HistoryViewModel(Data(), _session, new FakeNavigator());
        await history.LoadCommand.ExecuteAsync(null);

        Assert.Equal(MobileApiErrorMessages.Network, home.ErrorMessage);
        Assert.Null(home.Summary);
        Assert.Equal(MobileApiErrorMessages.Network, history.ErrorMessage);
        Assert.Null(history.History);
    }
}
