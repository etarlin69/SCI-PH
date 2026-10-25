using System.Text.Json;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Tests.Paramedic;

// Identificación por documento REAL (POST api/mobile/emergencies/{id}/identify/document) sobre la emergencia real; la
// huella sigue en mock y "continuar sin identificar" no llama al servidor.
public class DocumentIdentificationTests
{
    private static readonly Guid PatientId = Guid.NewGuid();

    private readonly FakeDocumentIdentificationBff _bff = new();
    private readonly MockParamedicApiService _mock = new();
    private readonly FakeNavigator _navigator = new();
    private readonly Guid _emergencyId = Guid.NewGuid();

    public DocumentIdentificationTests()
    {
        _bff.Patients["NationalId|001-1234567-8"] = (PatientId, "Ana Pérez");
        _bff.Patients["Passport|RD1234567"] = (PatientId, "Ana Pérez");
        // La emergencia la creó el servidor real: los pasos en mock la conocen por su id.
        _mock.Adopt(new EmergencyDto { Id = _emergencyId });
    }

    private HttpParamedicApiService Service() => new(_bff.Client(), _mock);

    private IdentifyPatientViewModel ViewModel(IFingerprintReaderService? fingerprint = null)
    {
        var vm = new IdentifyPatientViewModel(Service(), fingerprint ?? new CountingFingerprintReader(), _navigator) { EmergencyId = _emergencyId };
        vm.OpenDocumentFormCommand.Execute(null);
        return vm;
    }

    private JsonElement Body(int index) => JsonDocument.Parse(_bff.Bodies[index]).RootElement;

    [Fact]
    public async Task TheServicePostsTheContractAndReturnsTheSameEmergency()
    {
        var key = Guid.NewGuid();

        var emergency = await Service().IdentifyByDocumentAsync(_emergencyId, new IdentifyByDocumentDto
        {
            ClientRequestId = key,
            DocumentType = Features.Paramedic.Models.Enums.PatientDocumentType.NationalId,
            DocumentNumber = "001-1234567-8"
        });

        Assert.Equal($"POST /api/mobile/emergencies/{_emergencyId}/identify/document", Assert.Single(_bff.Paths));
        var body = Body(0);
        Assert.Equal(["clientRequestId", "documentType", "documentNumber"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(key, body.GetProperty("clientRequestId").GetGuid());
        Assert.Equal("NationalId", body.GetProperty("documentType").GetString());
        Assert.Equal((_emergencyId, PatientId, "Ana Pérez"), (emergency!.Id, emergency.Patient!.Id, emergency.Patient.DisplayName));
    }

    [Theory]
    [InlineData("Cédula", "NationalId", "001-1234567-8")]
    [InlineData("Pasaporte", "Passport", "RD1234567")]
    public async Task TheLabelMapsToTheContractTypeAndTheNumberIsSentAsTyped(string label, string type, string number)
    {
        var vm = ViewModel();
        vm.SelectedDocumentType = label;
        vm.DocumentNumber = number;

        await vm.SubmitDocumentCommand.ExecuteAsync(null);

        Assert.Equal(type, Body(0).GetProperty("documentType").GetString());
        Assert.Equal(number, Body(0).GetProperty("documentNumber").GetString());
        Assert.True(vm.IsIdentified);
    }

    [Fact]
    public void OnlyIdentityDocumentsAreOffered()
    {
        var vm = ViewModel();

        Assert.Equal(["Cédula", "Pasaporte"], vm.DocumentTypes);
        Assert.DoesNotContain("Otro", vm.DocumentTypes);
        Assert.Equal("Cédula", vm.SelectedDocumentType);
    }

    [Fact]
    public async Task AFoundPatientIdentifiesTheSameEmergencyAndContinuesToVitals()
    {
        var vm = ViewModel();
        vm.DocumentNumber = "001-1234567-8";

        await vm.SubmitDocumentCommand.ExecuteAsync(null);

        Assert.True(vm.IsIdentified);
        Assert.Equal("Ana Pérez", vm.PatientDisplayName);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(PatientId, _bff.IdentifiedPatient(_emergencyId)!.Value.Id);
        // Los pasos en mock ven la emergencia ya identificada, con su id real.
        Assert.Equal(PatientId, (await _mock.GetEmergencyAsync(_emergencyId))!.Patient!.Id);

        await vm.ContinueCommand.ExecuteAsync(null);
        var (route, parameters) = Assert.Single(_navigator.Navigations);
        Assert.Equal(ParamedicRoutes.VitalSigns, route);
        Assert.Equal(_emergencyId, parameters!["EmergencyId"]);
    }

    [Fact]
    public async Task NotFoundStaysOnIdentifyAndTheEmergencyCanContinueAsNn()
    {
        var vm = ViewModel();
        vm.SelectedDocumentType = "Pasaporte";
        vm.DocumentNumber = "XX0000000";

        await vm.SubmitDocumentCommand.ExecuteAsync(null);

        Assert.Equal(ParamedicMessages.PatientNotFoundByDocument, vm.ErrorMessage);
        Assert.False(vm.IsIdentified);
        Assert.True(vm.CanIdentify);
        Assert.Equal(("Pasaporte", "XX0000000"), (vm.SelectedDocumentType, vm.DocumentNumber));
        Assert.Empty(_navigator.Navigations);

        await vm.ContinueWithoutIdentifyingCommand.ExecuteAsync(null);
        Assert.Equal(_emergencyId, Assert.Single(_navigator.Navigations).Parameters!["EmergencyId"]);
        Assert.Single(_bff.Paths);
    }

    // After a 404 the number can be corrected: nothing was saved, so the same request id is still valid.
    [Fact]
    public async Task NotFoundCanBeCorrectedAndRetried()
    {
        var vm = ViewModel();
        vm.DocumentNumber = "001-0000000-0";
        await vm.SubmitDocumentCommand.ExecuteAsync(null);

        vm.DocumentNumber = "001-1234567-8";
        await vm.SubmitDocumentCommand.ExecuteAsync(null);

        Assert.True(vm.IsIdentified);
        Assert.Equal(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
    }

    [Fact]
    public async Task ANetworkFailureKeepsTheInputAndTheRetryKeepsTheRequestId()
    {
        var vm = ViewModel();
        vm.SelectedDocumentType = "Pasaporte";
        vm.DocumentNumber = "RD1234567";
        _bff.UnreachableLeft = 1;

        await vm.SubmitDocumentCommand.ExecuteAsync(null);

        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);
        Assert.False(vm.IsIdentified);
        Assert.Equal(("Pasaporte", "RD1234567"), (vm.SelectedDocumentType, vm.DocumentNumber));
        Assert.Equal(_emergencyId, vm.EmergencyId);

        await vm.SubmitDocumentCommand.ExecuteAsync(null);

        Assert.True(vm.IsIdentified);
        Assert.Equal(2, _bff.Paths.Count);
        Assert.Single(_bff.Bodies);
    }

    // The server identified, but the answer was lost: the retry with the same key is a replay, not a second identification.
    [Fact]
    public async Task ALostResponseIsReplayedWithTheSameRequestId()
    {
        var vm = ViewModel();
        vm.DocumentNumber = "001-1234567-8";
        _bff.LostResponsesLeft = 1;

        await vm.SubmitDocumentCommand.ExecuteAsync(null);
        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);

        await vm.SubmitDocumentCommand.ExecuteAsync(null);

        Assert.True(vm.IsIdentified);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
    }

    [Fact]
    public async Task AConflictShowsASafeMessageAndKeepsTheRequestId()
    {
        var vm = ViewModel();
        vm.DocumentNumber = "001-1234567-8";
        _bff.LostResponsesLeft = 1;
        await vm.SubmitDocumentCommand.ExecuteAsync(null);

        // Same key, another document: 409.
        vm.SelectedDocumentType = "Pasaporte";
        vm.DocumentNumber = "RD1234567";
        await vm.SubmitDocumentCommand.ExecuteAsync(null);

        Assert.Equal(ParamedicMessages.IdentificationConflict, vm.ErrorMessage);
        Assert.DoesNotContain("detail", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.IsIdentified);
        Assert.True(vm.CanIdentify);
        Assert.Equal(Body(0).GetProperty("clientRequestId").GetGuid(), Body(1).GetProperty("clientRequestId").GetGuid());
    }

    [Fact]
    public async Task TheFingerprintStaysMockAndNeverCallsTheServer()
    {
        var vm = ViewModel(new FoundFingerprintReader());

        await vm.IdentifyByFingerprintCommand.ExecuteAsync(null);

        Assert.True(vm.IsIdentified);
        Assert.Empty(_bff.Paths);
    }

    [Fact]
    public async Task ContinuingWithoutIdentifyingMakesNoIdentificationCall()
    {
        var api = new RecordingParamedicApi();
        var vm = new IdentifyPatientViewModel(api, new CountingFingerprintReader(), _navigator) { EmergencyId = _emergencyId };

        await vm.ContinueWithoutIdentifyingCommand.ExecuteAsync(null);

        Assert.Equal((0, 0), (api.IdentifyCalls, api.IdentifyByDocumentCalls));
        Assert.Equal(_emergencyId, Assert.Single(_navigator.Navigations).Parameters!["EmergencyId"]);
    }

    private sealed class FoundFingerprintReader : IFingerprintReaderService
    {
        public Task<FingerprintIdentifyResultDto> ReadAsync() =>
            Task.FromResult(new FingerprintIdentifyResultDto { Found = true, PatientId = Guid.NewGuid(), DisplayName = "Paciente Ficticio" });
    }
}
