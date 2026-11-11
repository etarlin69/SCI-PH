using System.Net;
using System.Text.Json;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Services;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Tests.Paramedic;

// Identificación por documento en dos pasos: "Buscar paciente" solo muestra un candidato (nombre, nacimiento, documento
// enmascarado) y la emergencia sigue NN; solo "Confirmar paciente" la identifica, con una clave por candidato que se conserva
// en los reintentos. Contra el contrato del Mobile BFF (FakeDocumentIdentificationBff).
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

    private IdentifyPatientViewModel ViewModel(string number = "001-1234567-8", string label = "Cédula")
    {
        var vm = new IdentifyPatientViewModel(Service(), new CountingFingerprintService(), _navigator) { EmergencyId = _emergencyId };
        vm.OpenDocumentFormCommand.Execute(null);
        vm.SelectedDocumentType = label;
        vm.DocumentNumber = number;
        return vm;
    }

    private JsonElement Body(int index) => JsonDocument.Parse(_bff.Bodies[index]).RootElement;

    private string Resolve => $"POST /api/mobile/emergencies/{_emergencyId}/identify/document/resolve";

    private string Confirm => $"POST /api/mobile/emergencies/{_emergencyId}/identify/document";

    [Fact]
    public async Task ResolvePostsOnlyTheDocumentAndConfirmPostsTheExistingContract()
    {
        var service = Service();
        var candidate = await service.ResolveDocumentCandidateAsync(_emergencyId, PatientDocumentType.NationalId, "001-1234567-8");
        var key = Guid.NewGuid();
        var emergency = await service.IdentifyByDocumentAsync(_emergencyId, new IdentifyByDocumentDto
        {
            ClientRequestId = key,
            DocumentType = PatientDocumentType.NationalId,
            DocumentNumber = "001-1234567-8"
        });

        Assert.Equal([Resolve, Confirm], _bff.Paths);
        Assert.Equal(["documentType", "documentNumber"], Body(0).EnumerateObject().Select(p => p.Name));
        Assert.Equal(["clientRequestId", "documentType", "documentNumber"], Body(1).EnumerateObject().Select(p => p.Name));
        Assert.Equal(("NationalId", "001-1234567-8", key), (Body(1).GetProperty("documentType").GetString(), Body(1).GetProperty("documentNumber").GetString(), Body(1).GetProperty("clientRequestId").GetGuid()));
        Assert.Equal(("Ana Pérez", new DateOnly(1985, 3, 14), "NationalId", "*******5678"),
            (candidate.DisplayName, candidate.DateOfBirth, candidate.DocumentType, candidate.DocumentValue));
        Assert.Equal((_emergencyId, PatientId), (emergency!.Id, emergency.Patient!.Id));
        // No patient id anywhere in the candidate model, nor the document in its text.
        Assert.DoesNotContain(typeof(DocumentCandidateDto).GetProperties(), p => p.Name.Contains("Id", StringComparison.Ordinal));
        Assert.DoesNotContain("5678", candidate.ToString());
    }

    [Theory]
    [InlineData("Cédula", "NationalId", "001-1234567-8")]
    [InlineData("Pasaporte", "Passport", "RD1234567")]
    public async Task TheLabelMapsToTheContractTypeAndTheNumberIsSentAsTypedToBothSteps(string label, string type, string number)
    {
        var vm = ViewModel(number, label);

        await vm.ResolveDocumentCommand.ExecuteAsync(null);
        await vm.ConfirmDocumentCommand.ExecuteAsync(null);

        foreach (var index in new[] { 0, 1 })
        {
            Assert.Equal((type, number), (Body(index).GetProperty("documentType").GetString(), Body(index).GetProperty("documentNumber").GetString()));
        }

        Assert.True(vm.IsIdentified);
    }

    [Fact]
    public void OnlyIdentityDocumentsAreOffered()
    {
        var vm = ViewModel();

        Assert.Equal(["Cédula", "Pasaporte"], vm.DocumentTypes);
        Assert.DoesNotContain("Otro", vm.DocumentTypes);
    }

    [Fact]
    public async Task ResolvingShowsTheCandidateAndDoesNotIdentify()
    {
        var vm = ViewModel();

        await vm.ResolveDocumentCommand.ExecuteAsync(null);

        Assert.Equal(DocumentIdentificationStep.Candidate, vm.DocumentStep);
        Assert.True(vm.HasDocumentCandidate);
        Assert.False(vm.IsDocumentEntry);
        Assert.Equal(("Ana Pérez", "14/03/1985", "Cédula *******5678"), (vm.DocumentCandidateName, vm.DocumentCandidateBirthDate, vm.DocumentCandidateDocument));
        Assert.False(vm.IsIdentified);
        Assert.Null(_bff.IdentifiedPatient(_emergencyId));
        Assert.Equal([Resolve], _bff.Paths);
        Assert.Empty(_navigator.Navigations);
        Assert.True(vm.ConfirmDocumentCommand.CanExecute(null));
    }

    [Fact]
    public async Task SearchingAnotherForgetsTheCandidateAndWritesNothing()
    {
        var vm = ViewModel();
        await vm.ResolveDocumentCommand.ExecuteAsync(null);

        vm.CancelDocumentCandidateCommand.Execute(null);

        Assert.Equal(DocumentIdentificationStep.Entry, vm.DocumentStep);
        Assert.Equal((null, null, null), (vm.DocumentCandidateName, vm.DocumentCandidateBirthDate, vm.DocumentCandidateDocument));
        Assert.False(vm.ConfirmDocumentCommand.CanExecute(null));
        Assert.False(vm.IsIdentified);
        Assert.Null(_bff.IdentifiedPatient(_emergencyId));
        Assert.Equal("001-1234567-8", vm.DocumentNumber);
        Assert.Equal([Resolve], _bff.Paths);
    }

    [Fact]
    public async Task ConfirmingIdentifiesTheSameEmergencyAndContinuesToVitals()
    {
        var vm = ViewModel();
        await vm.ResolveDocumentCommand.ExecuteAsync(null);

        await vm.ConfirmDocumentCommand.ExecuteAsync(null);

        Assert.True(vm.IsIdentified);
        Assert.Equal("Ana Pérez", vm.PatientDisplayName);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.HasDocumentCandidate);
        Assert.Equal(PatientId, _bff.IdentifiedPatient(_emergencyId)!.Value.Id);
        Assert.Equal(PatientId, (await _mock.GetEmergencyAsync(_emergencyId))!.Patient!.Id);

        await vm.ContinueCommand.ExecuteAsync(null);
        var (route, parameters) = Assert.Single(_navigator.Navigations);
        Assert.Equal((ParamedicRoutes.VitalSigns, (object)_emergencyId), (route, parameters!["EmergencyId"]));
    }

    [Fact]
    public async Task NotFoundStaysOnTheFormAndTheEmergencyCanContinueAsNn()
    {
        var vm = ViewModel("XX0000000", "Pasaporte");

        await vm.ResolveDocumentCommand.ExecuteAsync(null);

        Assert.Equal(ParamedicMessages.PatientNotFoundByDocument, vm.ErrorMessage);
        Assert.Equal(DocumentIdentificationStep.Entry, vm.DocumentStep);
        Assert.False(vm.IsIdentified);
        Assert.Equal(("Pasaporte", "XX0000000"), (vm.SelectedDocumentType, vm.DocumentNumber));

        await vm.ContinueWithoutIdentifyingCommand.ExecuteAsync(null);
        Assert.Equal(_emergencyId, Assert.Single(_navigator.Navigations).Parameters!["EmergencyId"]);
        Assert.Equal([Resolve], _bff.Paths);
    }

    [Fact]
    public async Task NotFoundCanBeCorrectedAndSearchedAgain()
    {
        var vm = ViewModel("001-0000000-0");
        await vm.ResolveDocumentCommand.ExecuteAsync(null);

        vm.DocumentNumber = "001-1234567-8";
        await vm.ResolveDocumentCommand.ExecuteAsync(null);

        Assert.Equal("Ana Pérez", vm.DocumentCandidateName);
        Assert.Null(vm.ErrorMessage);
    }

    // The server identified, but the answer was lost: the retry carries the SAME key and is a replay, not a second
    // identification.
    [Fact]
    public async Task ALostConfirmationIsRetriedWithTheSameKey()
    {
        var vm = ViewModel();
        await vm.ResolveDocumentCommand.ExecuteAsync(null);
        _bff.LostResponsesLeft = 1;

        await vm.ConfirmDocumentCommand.ExecuteAsync(null);
        Assert.Equal(ParamedicMessages.DocumentConfirmFailed, vm.ErrorMessage);
        Assert.Equal(DocumentIdentificationStep.Candidate, vm.DocumentStep);
        Assert.False(vm.IsIdentified);

        await vm.ConfirmDocumentCommand.ExecuteAsync(null);

        Assert.True(vm.IsIdentified);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(Body(1).GetProperty("clientRequestId").GetGuid(), Body(2).GetProperty("clientRequestId").GetGuid());
    }

    [Fact]
    public async Task ANetworkFailureWhileSearchingKeepsTheInput()
    {
        var vm = ViewModel("RD1234567", "Pasaporte");
        _bff.UnreachableLeft = 1;

        await vm.ResolveDocumentCommand.ExecuteAsync(null);

        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);
        Assert.Equal(DocumentIdentificationStep.Entry, vm.DocumentStep);
        Assert.Equal(("Pasaporte", "RD1234567"), (vm.SelectedDocumentType, vm.DocumentNumber));
        Assert.True(vm.ResolveDocumentCommand.CanExecute(null));
    }

    // A new candidate is a new confirmation: its key is not the one of the previous candidate.
    [Fact]
    public async Task ANewCandidateGetsANewKey()
    {
        var vm = ViewModel();
        await vm.ResolveDocumentCommand.ExecuteAsync(null);
        _bff.UnreachableLeft = 1;
        await vm.ConfirmDocumentCommand.ExecuteAsync(null);
        vm.CancelDocumentCandidateCommand.Execute(null);

        await vm.ResolveDocumentCommand.ExecuteAsync(null);
        await vm.ConfirmDocumentCommand.ExecuteAsync(null);

        Assert.True(vm.IsIdentified);
        var keys = _bff.Bodies.Select(b => JsonDocument.Parse(b).RootElement)
            .Where(b => b.TryGetProperty("clientRequestId", out _)).Select(b => b.GetProperty("clientRequestId").GetGuid()).ToList();
        Assert.Single(keys);
    }

    [Fact]
    public async Task ContinuingWithoutIdentifyingMakesNoIdentificationCall()
    {
        var api = new RecordingParamedicApi();
        var fingerprint = new CountingFingerprintService();
        var vm = new IdentifyPatientViewModel(api, fingerprint, _navigator) { EmergencyId = _emergencyId };

        await vm.ContinueWithoutIdentifyingCommand.ExecuteAsync(null);

        Assert.Equal((0, 0, 0), (fingerprint.Calls, api.IdentifyByDocumentCalls, api.ResolveDocumentCalls));
        Assert.Equal(_emergencyId, Assert.Single(_navigator.Navigations).Parameters!["EmergencyId"]);
    }
}

// El estado de la identificación por documento: búsquedas y confirmaciones en curso, errores, respuestas tardías y cambios de
// sesión, con un servidor guionado (ScriptedParamedicApi).
public class DocumentIdentificationStateTests
{
    private const string NnCode = "NN-0123456789ABCDEF0123456789ABCDEF";

    private readonly ScriptedParamedicApi _api = new();
    private readonly FakeNavigator _navigator = new();
    private readonly Guid _emergencyId = Guid.NewGuid();

    private EmergencyDto OnServer(string? patient = null)
    {
        var emergency = new EmergencyDto
        {
            Id = _emergencyId,
            TemporaryPatientCode = NnCode,
            Patient = patient is null ? null : new PatientSummaryDto { Id = Guid.NewGuid(), DisplayName = patient }
        };
        _api.Emergencies[_emergencyId] = emergency;
        return emergency;
    }

    private IdentifyPatientViewModel ViewModel(IClinicalSessionService? session = null)
    {
        OnServer();
        var vm = new IdentifyPatientViewModel(_api, new CountingFingerprintService(), _navigator, clinicalSession: session) { EmergencyId = _emergencyId };
        vm.OpenDocumentFormCommand.Execute(null);
        vm.DocumentNumber = "001-1234567-8";
        return vm;
    }

    private static Func<Task<T>> Fails<T>(HttpStatusCode status) => () => Task.FromException<T>(ScriptedParamedicApi.Status(status));

    private static (TaskCompletionSource<T> Pending, Func<Task<T>> Next) Held<T>()
    {
        var pending = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        return (pending, () => pending.Task);
    }

    private async Task<IdentifyPatientViewModel> WithCandidateAsync(IClinicalSessionService? session = null)
    {
        var vm = ViewModel(session);
        await vm.ResolveDocumentCommand.ExecuteAsync(null);
        Assert.Equal(DocumentIdentificationStep.Candidate, vm.DocumentStep);
        return vm;
    }

    [Fact]
    public async Task WhileSearchingADuplicateSearchIsNotPossible()
    {
        var vm = ViewModel();
        var (pending, next) = Held<DocumentCandidateDto>();
        _api.Resolves.Enqueue(next);

        var search = vm.ResolveDocumentCommand.ExecuteAsync(null);

        Assert.Equal(DocumentIdentificationStep.Resolving, vm.DocumentStep);
        Assert.True(vm.IsDocumentWorking);
        Assert.False(vm.IsDocumentEntry);
        Assert.False(vm.ResolveDocumentCommand.CanExecute(null));
        Assert.False(vm.ConfirmDocumentCommand.CanExecute(null));
        pending.SetResult(_api.DocumentCandidate);
        await search;

        Assert.Single(_api.ResolveRequests);
        Assert.Equal(DocumentIdentificationStep.Candidate, vm.DocumentStep);
    }

    [Fact]
    public async Task WhileConfirmingNothingElseCanBeTapped()
    {
        var vm = await WithCandidateAsync();
        var (pending, next) = Held<EmergencyDto?>();
        _api.Confirms.Enqueue(next);

        var confirm = vm.ConfirmDocumentCommand.ExecuteAsync(null);

        Assert.Equal(DocumentIdentificationStep.Confirming, vm.DocumentStep);
        Assert.True(vm.IsDocumentWorking);
        foreach (var command in new[] { vm.ConfirmDocumentCommand, vm.CancelDocumentCandidateCommand, vm.ContinueWithoutIdentifyingCommand, (System.Windows.Input.ICommand)vm.ResolveDocumentCommand })
        {
            Assert.False(command.CanExecute(null));
        }

        await vm.ConfirmDocumentCommand.ExecuteAsync(null);
        pending.SetResult(OnServer("Ana Pérez"));
        await confirm;

        Assert.Single(_api.ConfirmRequests);
        Assert.True(vm.IsIdentified);
    }

    [Fact]
    public async Task TheConfirmationSendsTheDocumentThatWasSearchedNotAnEditedOne()
    {
        var vm = await WithCandidateAsync();
        vm.DocumentNumber = "999";
        OnServer("Ana Pérez");

        await vm.ConfirmDocumentCommand.ExecuteAsync(null);

        var sent = Assert.Single(_api.ConfirmRequests);
        Assert.Equal((PatientDocumentType.NationalId, "001-1234567-8"), (sent.DocumentType, sent.DocumentNumber));
    }

    [Fact]
    public async Task ANotFoundAtConfirmationDiscardsTheCandidate()
    {
        var vm = await WithCandidateAsync();
        _api.Confirms.Enqueue(() => Task.FromResult<EmergencyDto?>(null));

        await vm.ConfirmDocumentCommand.ExecuteAsync(null);

        Assert.Equal((DocumentIdentificationStep.Entry, ParamedicMessages.DocumentCandidateGone), (vm.DocumentStep, vm.ErrorMessage));
        Assert.Null(vm.DocumentCandidateName);
        Assert.False(vm.IsIdentified);
    }

    // Another method identified the emergency between "Buscar" and "Confirmar": 409, the emergency is read again and its
    // patient is shown; the candidate never replaces it.
    [Fact]
    public async Task AConflictAfterAnotherIdentificationShowsTheServersPatient()
    {
        var vm = await WithCandidateAsync();
        _api.Confirms.Enqueue(Fails<EmergencyDto?>(HttpStatusCode.Conflict));
        OnServer("Luis Gómez");

        await vm.ConfirmDocumentCommand.ExecuteAsync(null);

        Assert.True(vm.IsIdentified);
        Assert.Equal("Luis Gómez", vm.PatientDisplayName);
        Assert.Equal(("Luis Gómez", (string?)NnCode), (vm.Identity!.Primary, vm.Identity.Secondary));
        Assert.Equal(ParamedicMessages.AlreadyIdentifiedByAnother, vm.ErrorMessage);
        Assert.False(vm.HasDocumentCandidate);
    }

    // 409 without a patient (handed off to the hospital, or the emergency changed): a safe conflict, the candidate is gone.
    [Fact]
    public async Task AConflictWithoutAPatientIsASafeMessage()
    {
        var vm = await WithCandidateAsync();
        _api.Confirms.Enqueue(Fails<EmergencyDto?>(HttpStatusCode.Conflict));

        await vm.ConfirmDocumentCommand.ExecuteAsync(null);

        Assert.Equal((DocumentIdentificationStep.Entry, ParamedicMessages.IdentificationConflict), (vm.DocumentStep, vm.ErrorMessage));
        Assert.False(vm.IsIdentified);
    }

    // A handed-off emergency already rejects the search: no candidate is ever shown.
    [Fact]
    public async Task AHandedOffEmergencyShowsNoCandidate()
    {
        var vm = ViewModel();
        _api.Resolves.Enqueue(Fails<DocumentCandidateDto>(HttpStatusCode.Conflict));

        await vm.ResolveDocumentCommand.ExecuteAsync(null);

        Assert.Equal((DocumentIdentificationStep.Entry, ParamedicMessages.IdentificationConflict), (vm.DocumentStep, vm.ErrorMessage));
        Assert.Null(vm.DocumentCandidateName);
        Assert.Empty(_api.ConfirmRequests);
    }

    // The hospital received the case while the page was open: the 409 re-reads the emergency, which says HandedOff. Every
    // identification action and "continue" disappear; only the explanation and "Volver al inicio" remain.
    [Fact]
    public async Task AConflictBecauseTheHospitalReceivedTheCaseHidesEveryAction()
    {
        var vm = ViewModel();
        _api.Emergencies[_emergencyId].Status = EmergencyStatus.HandedOff;
        _api.Resolves.Enqueue(Fails<DocumentCandidateDto>(HttpStatusCode.Conflict));

        await vm.ResolveDocumentCommand.ExecuteAsync(null);

        Assert.True(vm.IsHandedOff);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.CanIdentify);
        Assert.False(vm.ShowQrOption);
        Assert.False(vm.ShowDocumentPanel);
        Assert.False(vm.ShowFingerprintPanel);
        Assert.False(vm.ShowIdentifiedResult);
        Assert.Null(vm.DocumentCandidateName);
        Assert.Empty(_api.ConfirmRequests);
    }

    [Fact]
    public async Task AnEmergencyAlreadyHandedOffWhenThePageOpensOffersOnlyGoingHome()
    {
        var vm = ViewModel();
        _api.Emergencies[_emergencyId].Status = EmergencyStatus.HandedOff;

        await vm.RefreshIdentityAsync();

        Assert.True(vm.IsHandedOff);
        Assert.False(vm.CanIdentify);
        await vm.GoHomeCommand.ExecuteAsync(null);
        Assert.Equal(ParamedicRoutes.HomeRoot, Assert.Single(_navigator.Navigations).Route);
    }

    [Fact]
    public async Task AnEmergencyStillInTheFieldKeepsItsActions()
    {
        var vm = ViewModel();

        await vm.RefreshIdentityAsync();

        Assert.False(vm.IsHandedOff);
        Assert.True(vm.CanIdentify);
        Assert.True(vm.ShowDocumentPanel);
    }

    // The error is shown next to the form in use: inside the document card while it is open, at the top otherwise.
    [Fact]
    public async Task ADocumentErrorIsShownInsideTheDocumentForm()
    {
        var vm = ViewModel();
        _api.Resolves.Enqueue(Fails<DocumentCandidateDto>(HttpStatusCode.NotFound));

        await vm.ResolveDocumentCommand.ExecuteAsync(null);

        Assert.Equal(ParamedicMessages.PatientNotFoundByDocument, vm.ErrorMessage);
        Assert.True(vm.ShowDocumentError);
        Assert.False(vm.ShowTopError);
    }

    [Fact]
    public void WithoutTheDocumentFormTheErrorIsShownAtTheTop()
    {
        OnServer();
        var vm = new IdentifyPatientViewModel(_api, new CountingFingerprintService(), _navigator) { EmergencyId = _emergencyId };
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ErrorMessage = ParamedicMessages.FingerprintReadersFailed;

        Assert.True(vm.ShowTopError);
        Assert.False(vm.ShowDocumentError);
        Assert.Contains(nameof(vm.ShowTopError), changed);
    }

    [Fact]
    public async Task TooManyRequestsKeepsTheCandidateAndItsKey()
    {
        var vm = await WithCandidateAsync();
        _api.Confirms.Enqueue(Fails<EmergencyDto?>(HttpStatusCode.TooManyRequests));

        await vm.ConfirmDocumentCommand.ExecuteAsync(null);
        Assert.Equal(DocumentIdentificationStep.Candidate, vm.DocumentStep);
        Assert.Equal(MobileApiErrorMessages.For(new MobileApiException(MobileApiError.TooManyRequests, HttpStatusCode.TooManyRequests)), vm.ErrorMessage);
        OnServer("Ana Pérez");
        await vm.ConfirmDocumentCommand.ExecuteAsync(null);

        Assert.True(vm.IsIdentified);
        Assert.Equal(_api.ConfirmRequests[0].ClientRequestId, _api.ConfirmRequests[1].ClientRequestId);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AServerFailureAtConfirmationKeepsTheCandidateAndTheRetryKeepsTheKey(HttpStatusCode status)
    {
        var vm = await WithCandidateAsync();
        _api.Confirms.Enqueue(Fails<EmergencyDto?>(status));

        await vm.ConfirmDocumentCommand.ExecuteAsync(null);
        Assert.Equal((DocumentIdentificationStep.Candidate, ParamedicMessages.DocumentConfirmFailed), (vm.DocumentStep, vm.ErrorMessage));
        Assert.Equal("Ana Pérez", vm.DocumentCandidateName);
        OnServer("Ana Pérez");
        await vm.ConfirmDocumentCommand.ExecuteAsync(null);

        Assert.True(vm.IsIdentified);
        Assert.Equal(_api.ConfirmRequests[0].ClientRequestId, _api.ConfirmRequests[1].ClientRequestId);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ARejectedSearchStaysOnTheFormWithASafeMessage(HttpStatusCode status)
    {
        var vm = ViewModel();
        _api.Resolves.Enqueue(Fails<DocumentCandidateDto>(status));

        await vm.ResolveDocumentCommand.ExecuteAsync(null);

        Assert.Equal(DocumentIdentificationStep.Entry, vm.DocumentStep);
        Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage));
        Assert.DoesNotContain("Exception", vm.ErrorMessage);
        Assert.Null(vm.DocumentCandidateName);
    }

    [Fact]
    public async Task AForbiddenConfirmationDropsTheCandidate()
    {
        var vm = await WithCandidateAsync();
        _api.Confirms.Enqueue(Fails<EmergencyDto?>(HttpStatusCode.Forbidden));

        await vm.ConfirmDocumentCommand.ExecuteAsync(null);

        Assert.Equal(DocumentIdentificationStep.Entry, vm.DocumentStep);
        Assert.Null(vm.DocumentCandidateName);
        Assert.False(vm.IsIdentified);
    }

    // The page left while searching: the late candidate is never shown.
    [Fact]
    public async Task ALateSearchAfterLeavingThePageIsIgnored()
    {
        var vm = ViewModel();
        var (pending, next) = Held<DocumentCandidateDto>();
        _api.Resolves.Enqueue(next);
        var search = vm.ResolveDocumentCommand.ExecuteAsync(null);

        vm.DeactivateDocument();
        pending.SetResult(_api.DocumentCandidate);
        await search;

        Assert.Equal(DocumentIdentificationStep.Entry, vm.DocumentStep);
        Assert.Null(vm.DocumentCandidateName);
        Assert.True(vm.ResolveDocumentCommand.CanExecute(null));
    }

    // An older search answered after a newer one: only the newer candidate counts.
    [Fact]
    public async Task AnOlderSearchNeverReplacesANewerCandidate()
    {
        var vm = ViewModel();
        var (older, olderNext) = Held<DocumentCandidateDto>();
        _api.Resolves.Enqueue(olderNext);
        var first = vm.ResolveDocumentCommand.ExecuteAsync(null);
        vm.DeactivateDocument();
        _api.Resolves.Enqueue(() => Task.FromResult(new DocumentCandidateDto("Luis Gómez", null, "Passport", "*****4567")));
        vm.SelectedDocumentType = "Pasaporte";
        vm.DocumentNumber = "RD1234567";
        await vm.ResolveDocumentCommand.ExecuteAsync(null);

        older.SetResult(_api.DocumentCandidate);
        await first;

        Assert.Equal(("Luis Gómez", "Pasaporte *****4567", ParamedicMessages.CandidateBirthDateUnknown),
            (vm.DocumentCandidateName, vm.DocumentCandidateDocument, vm.DocumentCandidateBirthDate));
        OnServer("Luis Gómez");
        await vm.ConfirmDocumentCommand.ExecuteAsync(null);
        Assert.Equal((PatientDocumentType.Passport, "RD1234567"), (_api.ConfirmRequests[0].DocumentType, _api.ConfirmRequests[0].DocumentNumber));
    }

    // The page left while confirming: the late answer is not applied here; the candidate stays with the SAME key, and the
    // retry (a replay on the server) shows the identification.
    [Fact]
    public async Task ALateConfirmationAfterLeavingThePageIsNotAppliedAndTheRetryKeepsTheKey()
    {
        var vm = await WithCandidateAsync();
        var (pending, next) = Held<EmergencyDto?>();
        _api.Confirms.Enqueue(next);
        var confirm = vm.ConfirmDocumentCommand.ExecuteAsync(null);

        vm.DeactivateDocument();
        pending.SetResult(OnServer("Ana Pérez"));
        await confirm;

        Assert.False(vm.IsIdentified);
        Assert.Equal((DocumentIdentificationStep.Candidate, ParamedicMessages.DocumentConfirmFailed), (vm.DocumentStep, vm.ErrorMessage));
        await vm.ConfirmDocumentCommand.ExecuteAsync(null);
        Assert.True(vm.IsIdentified);
        Assert.Equal(_api.ConfirmRequests[0].ClientRequestId, _api.ConfirmRequests[1].ClientRequestId);
    }

    // Another account signed in while waiting: neither the candidate nor the identification of the previous session appears.
    [Fact]
    public async Task ALateSearchFromAnotherSessionIsIgnored()
    {
        var session = ParamedicSession.SignedIn();
        var vm = ViewModel(session);
        var (pending, next) = Held<DocumentCandidateDto>();
        _api.Resolves.Enqueue(next);
        var search = vm.ResolveDocumentCommand.ExecuteAsync(null);

        ParamedicSession.SignIn(session, Guid.NewGuid(), "otro");
        pending.SetResult(_api.DocumentCandidate);
        await search;

        Assert.Null(vm.DocumentCandidateName);
        Assert.False(vm.HasDocumentCandidate);
    }

    [Fact]
    public async Task ALateConfirmationFromAnotherSessionIsIgnored()
    {
        var session = ParamedicSession.SignedIn();
        var vm = await WithCandidateAsync(session);
        var (pending, next) = Held<EmergencyDto?>();
        _api.Confirms.Enqueue(next);
        var confirm = vm.ConfirmDocumentCommand.ExecuteAsync(null);

        session.Clear();
        pending.SetResult(OnServer("Ana Pérez"));
        await confirm;

        Assert.False(vm.IsIdentified);
        Assert.Null(vm.PatientDisplayName);
    }

    // NN in transport, identified from the transfer screen: the same emergency, and back to the transfer.
    [Fact]
    public async Task FromTheTransferTheConfirmationReturnsToTheTransfer()
    {
        var vm = ViewModel();
        vm.ReturnsToTransfer = true;
        await vm.ResolveDocumentCommand.ExecuteAsync(null);
        OnServer("Ana Pérez");
        await vm.ConfirmDocumentCommand.ExecuteAsync(null);

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(_emergencyId, _api.ConfirmRequests.Single() is { } _ ? vm.EmergencyId : Guid.Empty);
        Assert.Equal(ParamedicRoutes.Back, Assert.Single(_navigator.Navigations).Route);
    }
}
