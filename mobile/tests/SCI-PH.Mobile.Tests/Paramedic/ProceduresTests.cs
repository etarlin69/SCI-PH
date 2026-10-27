using System.Net;
using System.Text.Json;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Tests.Paramedic;

// Procedimientos REALES sobre la emergencia real: el catálogo fijo del servidor (GET api/mobile/catalogs/procedures) y un
// procedimiento ya realizado por envío (POST api/mobile/emergencies/{id}/procedures), con su propia clave y su hora,
// conservadas en los reintentos.
public class ProceduresTests
{
    private readonly FakeProceduresBff _bff = new();
    private readonly MockParamedicApiService _mock = new();
    private readonly FakeNavigator _navigator = new();
    private readonly Guid _emergencyId = Guid.NewGuid();

    public ProceduresTests() => _mock.Adopt(new EmergencyDto { Id = _emergencyId });

    private HttpParamedicApiService Service() => new(_bff.Client(), _mock);

    private HttpParamedicCatalogService Catalog() => new(_bff.Client());

    private ProceduresViewModel ViewModel() => new(Service(), Catalog(), _navigator) { EmergencyId = _emergencyId };

    private JsonElement Body(int index) => JsonDocument.Parse(_bff.Bodies[index]).RootElement;

    private async Task<ProceduresViewModel> LoadedAsync(string code = "OXYGEN-THERAPY")
    {
        var vm = ViewModel();
        await vm.LoadCatalogCommand.ExecuteAsync(null);
        vm.SelectedProcedure = vm.CatalogItems.Single(i => i.Code == code);
        return vm;
    }

    [Fact]
    public async Task TheCatalogComesFromTheServerNotFromTheMock()
    {
        var vm = ViewModel();

        await vm.LoadCatalogCommand.ExecuteAsync(null);

        Assert.Equal("GET /api/mobile/catalogs/procedures", Assert.Single(_bff.Paths));
        Assert.Equal(FakeProceduresBff.Catalog.Select(c => (c.Code, c.Name, c.Description)),
            vm.CatalogItems.Select(i => (i.Code, i.Name, i.Description)));
        Assert.DoesNotContain(vm.CatalogItems, i => i.Code.StartsWith("PROC-", StringComparison.Ordinal));
        Assert.False(vm.CatalogLoadFailed);

        vm.SelectedProcedure = vm.CatalogItems[0];
        Assert.True(vm.HasSelectedProcedureDescription);
        Assert.Equal(FakeProceduresBff.Catalog[0].Description, vm.SelectedProcedureDescription);
        vm.SelectedProcedure = vm.CatalogItems[2];
        Assert.False(vm.HasSelectedProcedureDescription);
    }

    [Fact]
    public async Task ACatalogThatCannotBeLoadedShowsASafeMessageAndCanBeRetried()
    {
        var vm = ViewModel();
        _bff.UnreachableLeft = 1;

        await vm.LoadCatalogCommand.ExecuteAsync(null);

        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);
        Assert.True(vm.CatalogLoadFailed);
        Assert.Empty(vm.CatalogItems);

        _bff.NextStatus = HttpStatusCode.InternalServerError;
        await vm.LoadCatalogCommand.ExecuteAsync(null);
        Assert.Equal(ParamedicMessages.ProceduresCatalogFailed, vm.ErrorMessage);

        await vm.LoadCatalogCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.CatalogLoadFailed);
        Assert.Equal(FakeProceduresBff.Catalog.Length, vm.CatalogItems.Count);
    }

    [Fact]
    public async Task TheProcedureIsSentAsTheExactContract()
    {
        var vm = await LoadedAsync();
        vm.Outcome = "  SpO2 97 % tras 5 min ";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal($"POST /api/mobile/emergencies/{_emergencyId}/procedures", _bff.Paths[^1]);
        var body = Body(0);
        Assert.Equal(["clientRequestId", "procedureCode", "performedAt", "outcome"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(("OXYGEN-THERAPY", "SpO2 97 % tras 5 min"), (body.GetProperty("procedureCode").GetString(), body.GetProperty("outcome").GetString()));
        Assert.EndsWith("+00:00", body.GetProperty("performedAt").GetString());
        Assert.NotEqual(Guid.Empty, body.GetProperty("clientRequestId").GetGuid());
        foreach (var field in new[] { "name", "description", "codeSystem", "notes", "staffId", "completedAt", "status" })
        {
            Assert.False(body.TryGetProperty(field, out _), field);
        }
    }

    [Fact]
    public async Task WithoutAnOutcomeTheFieldIsNotSent()
    {
        var vm = await LoadedAsync();
        vm.Outcome = "   ";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["clientRequestId", "procedureCode", "performedAt"], Body(0).EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task AProcedureMustBeChosenAndTheOutcomeRespectsTheServerLimit()
    {
        var vm = ViewModel();
        await vm.LoadCatalogCommand.ExecuteAsync(null);
        Assert.False(vm.SaveCommand.CanExecute(null));

        vm.SelectedProcedure = vm.CatalogItems[0];
        Assert.True(vm.SaveCommand.CanExecute(null));

        vm.Outcome = new string('a', ProceduresViewModel.OutcomeMaxLength + 1);
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.Outcome = new string('a', ProceduresViewModel.OutcomeMaxLength);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task ARecordedProcedureGoesToMedicationsWithTheSameEmergency()
    {
        var vm = await LoadedAsync();

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(HttpStatusCode.Created, Assert.Single(_bff.Answers));
        Assert.Null(vm.ErrorMessage);
        var (route, parameters) = Assert.Single(_navigator.Navigations);
        Assert.Equal(ParamedicRoutes.Medications, route);
        Assert.Equal(_emergencyId, parameters!["EmergencyId"]);
        Assert.Null(vm.SelectedProcedure);
        Assert.Equal(1, vm.SavedCount);

        // The next procedure is a new one: new key and new time.
        vm.SelectedProcedure = vm.CatalogItems.Single(i => i.Code == "CPR");
        await Task.Delay(20);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotEqual(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
        Assert.NotEqual(Body(0).GetProperty("performedAt").GetString(), Body(1).GetProperty("performedAt").GetString());
        Assert.Equal(2, _bff.Recorded);
    }

    [Fact]
    public async Task ANetworkFailureKeepsTheProcedureKeyAndTimeForTheRetry()
    {
        var vm = await LoadedAsync();
        vm.Outcome = "Sin complicaciones";
        _bff.UnreachableLeft = 1;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);
        Assert.Equal(("OXYGEN-THERAPY", "Sin complicaciones"), (vm.SelectedProcedure!.Code, vm.Outcome));
        Assert.Empty(_navigator.Navigations);

        await Task.Delay(20);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Single(_bff.Bodies);
        Assert.Equal(1, _bff.Recorded);
        Assert.Single(_navigator.Navigations);
    }

    // The server recorded it but the answer was lost: the retry with the same key and time is a replay (200).
    [Fact]
    public async Task ALostResponseIsReplayedWithTheSameKeyAndTimeAndGoesToMedications()
    {
        var vm = await LoadedAsync();
        _bff.LostResponsesLeft = 1;

        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);
        await Task.Delay(20);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.OK], _bff.Answers);
        Assert.Equal(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
        Assert.Equal(Body(0).GetProperty("performedAt").GetString(), Body(1).GetProperty("performedAt").GetString());
        Assert.Equal(1, _bff.Recorded);
        Assert.Equal(ParamedicRoutes.Medications, Assert.Single(_navigator.Navigations).Route);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, MobileApiErrorMessages.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden, MobileApiErrorMessages.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ParamedicMessages.EmergencyNotFound)]
    [InlineData(HttpStatusCode.InternalServerError, ParamedicMessages.RegisterProcedureFailed)]
    public async Task ARejectedProcedureStaysWithASafeMessageAndKeepsItsKey(HttpStatusCode status, string message)
    {
        var vm = await LoadedAsync();
        _bff.NextStatus = status;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(message, vm.ErrorMessage);
        Assert.Equal("OXYGEN-THERAPY", vm.SelectedProcedure!.Code);
        Assert.Empty(_navigator.Navigations);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
        Assert.Equal(Body(0).GetProperty("performedAt").GetString(), Body(1).GetProperty("performedAt").GetString());
    }

    // Same key with other data (changed after a lost answer): 409, a safe message, no new key in silence.
    [Fact]
    public async Task AConflictShowsASafeMessageAndKeepsTheKey()
    {
        var vm = await LoadedAsync();
        _bff.LostResponsesLeft = 1;
        await vm.SaveCommand.ExecuteAsync(null);

        vm.Outcome = "Otro resultado";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ParamedicMessages.ProcedureConflict, vm.ErrorMessage);
        Assert.Empty(_navigator.Navigations);
        Assert.Equal(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
        Assert.Equal(1, _bff.Recorded);
    }

    [Fact]
    public async Task TheRegisteredProcedureIsNotKeptInTheMock()
    {
        var vm = await LoadedAsync();

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty((await _mock.GetEmergencyAsync(_emergencyId))!.Procedures);
        Assert.Equal(1, _bff.Recorded);
    }
}
