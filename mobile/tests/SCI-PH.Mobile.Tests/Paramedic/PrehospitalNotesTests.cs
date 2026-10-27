using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Dialogs;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Tests.Paramedic;

// Observaciones REALES (POST api/mobile/emergencies/{id}/notes) sobre la emergencia real: una por envío, con su propia
// clave y su hora automática, conservadas en los reintentos. Tras registrar, la pantalla queda lista para otra
// observación; "Continuar" lleva al traslado.
public class PrehospitalNotesTests
{
    private const string Text = "Paciente consciente, orientado.";
    private readonly FakeNotesBff _bff = new();
    private readonly MockParamedicApiService _mock = new();
    private readonly FakeNavigator _navigator = new();
    private readonly Guid _emergencyId = Guid.NewGuid();

    public PrehospitalNotesTests() => _mock.Adopt(new EmergencyDto { Id = _emergencyId });

    private HttpParamedicApiService Service() => new(_bff.Client(), _mock);

    private PrehospitalNotesViewModel ViewModel(string content = Text) => new(Service(), _navigator) { EmergencyId = _emergencyId, Content = content };

    private JsonElement Body(int index) => JsonDocument.Parse(_bff.Bodies[index]).RootElement;

    [Fact]
    public void TheParamedicFeatureRegistersTheRealNotesFlow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMobileApiClient>(_bff.Client());
        services.AddSingleton<IAppDialogService>(new FakeDialogs());
        services.AddSingleton<IAppNavigator>(_navigator);

        services.AddParamedicFeature();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<HttpParamedicApiService>(provider.GetRequiredService<IParamedicApiService>());
        Assert.NotNull(provider.GetRequiredService<PrehospitalNotesViewModel>());
        Assert.Equal("Notes", ParamedicRoutes.Notes);
    }

    [Fact]
    public async Task TheNoteIsSentAsTheExactContractWithoutType()
    {
        var vm = ViewModel($"  {Text}\n");

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal($"POST /api/mobile/emergencies/{_emergencyId}/notes", Assert.Single(_bff.Paths));
        var body = Body(0);
        Assert.Equal(["clientRequestId", "occurredAt", "content"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(Text, body.GetProperty("content").GetString());
        Assert.EndsWith("+00:00", body.GetProperty("occurredAt").GetString());
        foreach (var field in new[] { "type", "noteType", "authorStaffId", "patientId", "clinicId", "createdAtUtc", "encounterId" })
        {
            Assert.False(body.TryGetProperty(field, out _), field);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public void AnEmptyObservationCannotBeSaved(string content)
    {
        Assert.False(ViewModel(content).SaveCommand.CanExecute(null));
    }

    [Fact]
    public void TheMobileLimitIs4000CharactersWithACounter()
    {
        var vm = ViewModel(new string('a', PrehospitalNotesViewModel.ContentMaxLength));
        Assert.True(vm.SaveCommand.CanExecute(null));
        Assert.Equal("4000/4000", vm.ContentCounter);

        vm.Content = new string('a', PrehospitalNotesViewModel.ContentMaxLength + 1);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    // occurredAt is automatic and read-only: pending until the first attempt, then fixed for the retries.
    [Fact]
    public async Task OccurredAtIsGeneratedOnTheFirstAttemptAndKeptForTheRetry()
    {
        var vm = ViewModel();
        Assert.Equal(ParamedicMessages.NoteTimePending, vm.OccurredAtText);
        _bff.UnreachableLeft = 1;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);
        Assert.NotEqual(ParamedicMessages.NoteTimePending, vm.OccurredAtText);
        var shown = vm.OccurredAtText;
        Assert.Equal(Text, vm.Content);

        await Task.Delay(20);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Single(_bff.Bodies);
        Assert.Equal(1, _bff.Recorded);
        Assert.Contains(shown, vm.SavedSummaries[0]);
    }

    // The server recorded it but the answer was lost: the retry with the same key and time is a replay (200), one note.
    [Fact]
    public async Task ALostResponseIsReplayedWithTheSameKeyAndTime()
    {
        var vm = ViewModel();
        _bff.LostResponsesLeft = 1;

        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);
        await Task.Delay(20);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.OK], _bff.Answers);
        Assert.Equal(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
        Assert.Equal(Body(0).GetProperty("occurredAt").GetString(), Body(1).GetProperty("occurredAt").GetString());
        Assert.Equal((1, 1), (_bff.Recorded, vm.SavedCount));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, MobileApiErrorMessages.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden, MobileApiErrorMessages.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ParamedicMessages.EmergencyNotFound)]
    [InlineData(HttpStatusCode.Conflict, ParamedicMessages.NoteConflict)]
    [InlineData(HttpStatusCode.InternalServerError, ParamedicMessages.RegisterNoteFailed)]
    public async Task ARejectedNoteStaysWithASafeMessageAndKeepsItsKeyAndTime(HttpStatusCode status, string message)
    {
        var vm = ViewModel();
        _bff.NextStatus = status;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(message, vm.ErrorMessage);
        Assert.DoesNotContain("Forced", vm.ErrorMessage);
        Assert.Equal(Text, vm.Content);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
        Assert.Equal(Body(0).GetProperty("occurredAt").GetString(), Body(1).GetProperty("occurredAt").GetString());
    }

    // Text changed after an uncertain result: 409, a safe message, no new key in silence; the paramedic can still continue.
    [Fact]
    public async Task AConflictAfterChangingTheTextKeepsTheKeyAndTheParamedicCanContinue()
    {
        var vm = ViewModel();
        _bff.LostResponsesLeft = 1;
        await vm.SaveCommand.ExecuteAsync(null);

        vm.Content = Text + " Editado.";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ParamedicMessages.NoteConflict, vm.ErrorMessage);
        Assert.Equal(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
        Assert.Equal(1, _bff.Recorded);

        await vm.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(ParamedicRoutes.Transfer, Assert.Single(_navigator.Navigations).Route);
    }

    // Success: confirmation, the note in the session list, an empty editor and a new key and time for the next note.
    [Fact]
    public async Task SeveralNotesAreRecordedEachWithItsOwnKeyAndTime()
    {
        var vm = ViewModel();

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal((null, ParamedicMessages.NoteRecorded), (vm.ErrorMessage, vm.SuccessMessage));
        Assert.Equal(("", ParamedicMessages.NoteTimePending, 1), (vm.Content, vm.OccurredAtText, vm.SavedCount));
        Assert.EndsWith(Text, vm.SavedSummaries[0]);
        Assert.Empty(_navigator.Navigations);

        vm.Content = "Segunda observación.";
        await Task.Delay(1100);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(2, _bff.Recorded);
        Assert.Equal(2, vm.SavedCount);
        Assert.EndsWith("Segunda observación.", vm.SavedSummaries[0]);
        Assert.NotEqual(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
        Assert.NotEqual(Body(0).GetProperty("occurredAt").GetString(), Body(1).GetProperty("occurredAt").GetString());
    }

    [Fact]
    public async Task ContinueGoesToTheTransferOfTheSameEmergency()
    {
        var vm = ViewModel();
        await vm.SaveCommand.ExecuteAsync(null);

        await vm.ContinueCommand.ExecuteAsync(null);

        var (route, parameters) = Assert.Single(_navigator.Navigations);
        Assert.Equal((ParamedicRoutes.Transfer, _emergencyId), (route, parameters!["EmergencyId"]));
    }

    [Fact]
    public async Task TheNoteGoesToTheServerNotToTheMock()
    {
        var vm = ViewModel();

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, _bff.Recorded);
        Assert.Single(_bff.Paths);
    }
}
