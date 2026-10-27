using System.Net;
using System.Text.Json;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Services;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Tests.Paramedic;

// Medicamentos REALES sobre la emergencia real: el catálogo de activos del servidor (GET api/mobile/catalogs/medications)
// y una dosis ya administrada por envío (POST api/mobile/emergencies/{id}/medication-administrations), sin prescripción,
// con su propia clave y su hora, conservadas en los reintentos. Tras registrar, la pantalla queda lista para otra dosis.
public class MedicationsTests
{
    private readonly FakeMedicationsBff _bff = new();
    private readonly MockParamedicApiService _mock = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly Guid _emergencyId = Guid.NewGuid();

    public MedicationsTests() => _mock.Adopt(new EmergencyDto { Id = _emergencyId });

    private HttpParamedicApiService Service() => new(_bff.Client(), _mock);

    private MedicationsViewModel ViewModel() => new(Service(), new HttpParamedicCatalogService(_bff.Client()), _dialogs) { EmergencyId = _emergencyId };

    private JsonElement Body(int index) => JsonDocument.Parse(_bff.Bodies[index]).RootElement;

    private static void Fill(MedicationsViewModel vm, Guid medication, string dose = "500", string unit = "mg")
    {
        vm.SelectedMedication = vm.CatalogItems.Single(m => m.Id == medication);
        vm.Dose = dose;
        vm.SelectedDoseUnit = unit;
    }

    private async Task<MedicationsViewModel> FilledAsync(string dose = "500", string unit = "mg")
    {
        var vm = ViewModel();
        await vm.LoadCatalogCommand.ExecuteAsync(null);
        Fill(vm, FakeMedicationsBff.Paracetamol, dose, unit);
        return vm;
    }

    [Fact]
    public async Task TheCatalogComesFromTheServerWithPresentationAndConcentration()
    {
        var vm = ViewModel();

        await vm.LoadCatalogCommand.ExecuteAsync(null);

        Assert.Equal("GET /api/mobile/catalogs/medications", Assert.Single(_bff.Paths));
        Assert.Equal([FakeMedicationsBff.SterileWater, FakeMedicationsBff.Metamizol, FakeMedicationsBff.Paracetamol], vm.CatalogItems.Select(m => m.Id));
        Assert.Equal(["Agua estéril", "Metamizol · Solución inyectable · 1 g/2 mL", "Paracetamol · Tableta · 500 mg"],
            vm.CatalogItems.Select(m => m.DisplayName));
        Assert.Equal([null, MedicationRoute.Intravenous, MedicationRoute.Oral], vm.CatalogItems.Select(m => m.DefaultRoute));
        Assert.DoesNotContain(vm.CatalogItems, m => m.Name.Contains("simulado", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ACatalogThatCannotBeLoadedShowsASafeMessageAndCanBeRetried()
    {
        var vm = ViewModel();
        _bff.UnreachableLeft = 1;

        await vm.LoadCatalogCommand.ExecuteAsync(null);
        Assert.Equal((MobileApiErrorMessages.Network, true), (vm.ErrorMessage, vm.CatalogLoadFailed));

        _bff.NextStatus = HttpStatusCode.InternalServerError;
        await vm.LoadCatalogCommand.ExecuteAsync(null);
        Assert.Equal(ParamedicMessages.MedicationsCatalogFailed, vm.ErrorMessage);

        await vm.LoadCatalogCommand.ExecuteAsync(null);
        Assert.Equal((null, false, 3), (vm.ErrorMessage, vm.CatalogLoadFailed, vm.CatalogItems.Count));
    }

    // The default route is preselected; the paramedic can change it. Without a default, nothing is preselected.
    [Fact]
    public async Task TheDefaultRouteIsPreselectedAndCanBeOverridden()
    {
        var vm = ViewModel();
        await vm.LoadCatalogCommand.ExecuteAsync(null);

        vm.SelectedMedication = vm.CatalogItems.Single(m => m.Id == FakeMedicationsBff.Metamizol);
        Assert.Equal(MedicationRoute.Intravenous, vm.SelectedRoute!.Value);

        vm.SelectedRoute = vm.Routes.Single(r => r.Value == MedicationRoute.Intramuscular);
        vm.Dose = "1";
        vm.SelectedDoseUnit = "g";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Intramuscular", Body(0).GetProperty("route").GetString());

        var other = ViewModel();
        await other.LoadCatalogCommand.ExecuteAsync(null);
        other.SelectedMedication = other.CatalogItems.Single(m => m.Id == FakeMedicationsBff.SterileWater);
        Assert.Null(other.SelectedRoute);
    }

    // Every UI route maps to the exact server enum name; the old Spanish strings never travel.
    [Fact]
    public async Task EveryRouteTravelsAsTheServerEnumName()
    {
        var vm = ViewModel();
        Assert.Equal(
            ["Oral", "Intravenous", "Intramuscular", "Subcutaneous", "Inhalation", "Topical", "Sublingual", "Rectal", "Nasal",
             "Ophthalmic", "Otic", "Transdermal", "Other"],
            vm.Routes.Select(r => r.Value.ToString()));
        await vm.LoadCatalogCommand.ExecuteAsync(null);

        foreach (var route in vm.Routes)
        {
            Fill(vm, FakeMedicationsBff.SterileWater);
            vm.SelectedRoute = route;
            await vm.SaveCommand.ExecuteAsync(null);
        }

        Assert.Equal(vm.Routes.Select(r => r.Value.ToString()), _bff.Bodies.Select(b => JsonDocument.Parse(b).RootElement.GetProperty("route").GetString()));
        Assert.DoesNotContain(_bff.Bodies, b => b.Contains("Intranasal") || b.Contains("Inhalatoria"));
    }

    [Fact]
    public async Task TheDoseIsSentAsTheExactContract()
    {
        var vm = await FilledAsync("2,5", "mg");
        vm.Notes = "  Con agua ";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal($"POST /api/mobile/emergencies/{_emergencyId}/medication-administrations", _bff.Paths[^1]);
        var body = Body(0);
        Assert.Equal(["clientRequestId", "medicationId", "dose", "doseUnit", "route", "administeredAt", "notes"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal((FakeMedicationsBff.Paracetamol, 2.5m, "mg", "Oral", "Con agua"),
            (body.GetProperty("medicationId").GetGuid(), body.GetProperty("dose").GetDecimal(), body.GetProperty("doseUnit").GetString(),
             body.GetProperty("route").GetString(), body.GetProperty("notes").GetString()));
        Assert.Equal(JsonValueKind.Number, body.GetProperty("dose").ValueKind);
        Assert.EndsWith("+00:00", body.GetProperty("administeredAt").GetString());
        foreach (var field in new[] { "prescriptionId", "prescriptionDetailId", "medicationName", "name", "presentation", "concentration", "defaultRoute", "staffId", "status", "medicationCode" })
        {
            Assert.False(body.TryGetProperty(field, out _), field);
        }
    }

    [Fact]
    public async Task WithoutNotesTheFieldIsNotSent()
    {
        var vm = await FilledAsync();
        vm.Notes = "   ";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(Body(0).TryGetProperty("notes", out _));
    }

    [Theory]
    [InlineData("500", 500)]
    [InlineData("2,5", 2.5)]
    [InlineData("2.5", 2.5)]
    [InlineData(" 0,125 ", 0.125)]
    [InlineData("999999999,999", 999999999.999)]
    public void ADoseWithCommaOrPointIsParsedSafely(string text, double expected)
    {
        var vm = ViewModel();
        vm.Dose = text;

        Assert.False(vm.HasDoseError);
        Assert.Equal((decimal)expected, MedicationsViewModel.ParseDose(text));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1,2345")]
    [InlineData("1000000000")]
    [InlineData("abc")]
    [InlineData("1.000,5")]
    public async Task AnInvalidDoseIsFlaggedAndCannotBeSaved(string text)
    {
        var vm = await FilledAsync(text);

        Assert.True(vm.HasDoseError);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task EverythingIsRequiredExceptTheNotesAndTheNotesRespectTheServerLimit()
    {
        var vm = ViewModel();
        await vm.LoadCatalogCommand.ExecuteAsync(null);
        Assert.False(vm.SaveCommand.CanExecute(null));

        vm.SelectedMedication = vm.CatalogItems.Single(m => m.Id == FakeMedicationsBff.Paracetamol);   // route Oral preselected
        vm.Dose = "500";
        Assert.False(vm.SaveCommand.CanExecute(null));                                                   // no unit yet
        vm.SelectedDoseUnit = "mg";
        Assert.True(vm.SaveCommand.CanExecute(null));

        vm.Notes = new string('n', MedicationsViewModel.NotesMaxLength + 1);
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.Notes = new string('n', MedicationsViewModel.NotesMaxLength);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void TheUnitListIsOnlyAnAidAndOtherOpensFreeText()
    {
        var vm = ViewModel();
        Assert.Equal(["mg", "mcg", "g", "mL", "UI", "gotas", "inhalaciones", "Otra"], vm.DoseUnits);

        vm.SelectedDoseUnit = "inhalaciones";
        Assert.Equal(("inhalaciones", false), (vm.EffectiveDoseUnit, vm.IsCustomDoseUnit));

        vm.SelectedDoseUnit = MedicationsViewModel.OtherDoseUnit;
        Assert.True(vm.IsCustomDoseUnit);
        Assert.Null(vm.EffectiveDoseUnit);
        vm.CustomDoseUnit = "  ampollas ";
        Assert.Equal("ampollas", vm.EffectiveDoseUnit);
        vm.CustomDoseUnit = new string('u', MedicationsViewModel.DoseUnitMaxLength + 1);
        Assert.True(vm.HasDoseUnitError);
        Assert.Null(vm.EffectiveDoseUnit);
    }

    [Fact]
    public async Task AnOtherUnitIsSentTrimmedAsWritten()
    {
        var vm = await FilledAsync(unit: MedicationsViewModel.OtherDoseUnit);
        vm.CustomDoseUnit = "  ampollas ";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("ampollas", Body(0).GetProperty("doseUnit").GetString());
    }

    // Success: confirmation, empty form, still on this page (no navigation) and the next dose gets a new key and time.
    [Fact]
    public async Task ARecordedDoseClearsTheFormAndStaysForTheNextDose()
    {
        var vm = await FilledAsync();
        vm.Notes = "Con agua";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(HttpStatusCode.Created, Assert.Single(_bff.Answers));
        Assert.Equal((null, ParamedicMessages.MedicationRecorded), (vm.ErrorMessage, vm.SuccessMessage));
        Assert.Equal(((MedicationCatalogItemDto?)null, "", (string?)null, "", (MedicationRouteOption?)null),
            (vm.SelectedMedication, vm.Dose, vm.SelectedDoseUnit, vm.Notes, vm.SelectedRoute));
        Assert.Equal(1, vm.SavedCount);
        Assert.Empty(_dialogs.Shown);

        Fill(vm, FakeMedicationsBff.Paracetamol, "250");
        await Task.Delay(20);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotEqual(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
        Assert.NotEqual(Body(0).GetProperty("administeredAt").GetString(), Body(1).GetProperty("administeredAt").GetString());
        Assert.Equal(2, _bff.Recorded);
    }

    [Fact]
    public async Task ANetworkFailureKeepsTheDoseKeyAndTimeForTheRetry()
    {
        var vm = await FilledAsync();
        _bff.UnreachableLeft = 1;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);
        Assert.Null(vm.SuccessMessage);
        Assert.Equal((FakeMedicationsBff.Paracetamol, "500", "mg"), (vm.SelectedMedication!.Id, vm.Dose, vm.SelectedDoseUnit));

        await Task.Delay(20);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Single(_bff.Bodies);
        Assert.Equal(1, _bff.Recorded);
        Assert.Equal(ParamedicMessages.MedicationRecorded, vm.SuccessMessage);
    }

    // The server recorded it but the answer was lost: the retry with the same key and time is a replay (200), one dose.
    [Fact]
    public async Task ALostResponseIsReplayedWithTheSameKeyAndTime()
    {
        var vm = await FilledAsync();
        _bff.LostResponsesLeft = 1;

        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);
        await Task.Delay(20);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.OK], _bff.Answers);
        Assert.Equal(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
        Assert.Equal(Body(0).GetProperty("administeredAt").GetString(), Body(1).GetProperty("administeredAt").GetString());
        Assert.Equal(1, _bff.Recorded);
        Assert.Equal(1, vm.SavedCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, MobileApiErrorMessages.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden, MobileApiErrorMessages.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ParamedicMessages.MedicationOrEmergencyNotFound)]
    [InlineData(HttpStatusCode.Conflict, ParamedicMessages.MedicationAdministrationConflict)]
    [InlineData(HttpStatusCode.InternalServerError, ParamedicMessages.RegisterMedicationFailed)]
    public async Task ARejectedDoseStaysWithASafeMessageAndKeepsItsKey(HttpStatusCode status, string message)
    {
        var vm = await FilledAsync();
        _bff.NextStatus = status;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(message, vm.ErrorMessage);
        Assert.DoesNotContain("Forced", vm.ErrorMessage);
        Assert.Equal("500", vm.Dose);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
        Assert.Equal(Body(0).GetProperty("administeredAt").GetString(), Body(1).GetProperty("administeredAt").GetString());
    }

    // Data changed after an uncertain result: 409, a safe message, no new key in silence; the paramedic can still leave.
    [Fact]
    public async Task AConflictAfterChangingTheDataKeepsTheKeyAndTheParamedicCanContinue()
    {
        var vm = await FilledAsync();
        _bff.LostResponsesLeft = 1;
        await vm.SaveCommand.ExecuteAsync(null);

        vm.Dose = "650";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ParamedicMessages.MedicationAdministrationConflict, vm.ErrorMessage);
        Assert.Equal(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
        Assert.Equal(1, _bff.Recorded);

        await vm.ContinueCommand.ExecuteAsync(null);
        Assert.Single(_dialogs.Shown);
    }

    [Fact]
    public async Task TheRecordedDoseIsNotKeptInTheMock()
    {
        var vm = await FilledAsync();

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty((await _mock.GetEmergencyAsync(_emergencyId))!.Medications);
        Assert.Equal(1, _bff.Recorded);
    }
}
