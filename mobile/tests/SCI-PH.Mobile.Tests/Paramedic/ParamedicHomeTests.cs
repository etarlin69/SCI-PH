using System.Net;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Features.Auth;
using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Paramedic.Models;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Tests.Paramedic;

// Inicio del paramédico: "Nueva emergencia" y "Emergencias activas" (la lista del servidor), retomar un caso según su
// estado ACTUAL en el servidor, y la lista siempre de la cuenta que la pidió.
public class ParamedicHomeTests
{
    private const string NnCode = "NN-0123456789ABCDEF0123456789ABCDEF";
    private const string OtherNnCode = "NN-FEDCBA9876543210FEDCBA9876543210";

    private readonly ScriptedParamedicApi _api = new();
    private readonly FakeNavigator _navigator = new();
    private readonly Common.Authentication.ClinicalSessionService _session = ParamedicSession.SignedIn();

    private ParamedicHomeViewModel ViewModel() => new(_api, _navigator, _session);

    private static ActiveEmergencyDto Active(
        string? code = NnCode, string? patient = null, string status = "InProgress", TransferStatus? transfer = null, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        TemporaryPatientCode = code,
        PatientDisplayName = patient,
        Status = status,
        StartedAt = DateTime.UtcNow.AddMinutes(-15),
        ChiefComplaint = "Politrauma",
        ActiveTransferStatus = transfer
    };

    private EmergencyDto OnServer(Guid id, string? patient = null, TransferStatus? transfer = null, EmergencyStatus status = EmergencyStatus.Open)
    {
        var emergency = new EmergencyDto
        {
            Id = id,
            TemporaryPatientCode = NnCode,
            Status = status,
            Patient = patient is null ? null : new PatientSummaryDto { Id = Guid.NewGuid(), DisplayName = patient },
            ActiveTransfer = transfer is null ? null : new TransferDto { Id = Guid.NewGuid(), Status = transfer.Value }
        };
        _api.Emergencies[id] = emergency;
        return emergency;
    }

    [Fact]
    public void AfterLoginTheParamedicLandsOnTheHomeAsTheRoot()
    {
        Assert.Equal("//" + ParamedicRoutes.Home, AuthRoutes.ClinicalArea);
        Assert.Equal(AuthRoutes.ClinicalArea, ParamedicRoutes.HomeRoot);
    }

    [Fact]
    public async Task WhileLoadingTheHomeIsBusyAndNotYetEmpty()
    {
        _api.HoldLists = true;
        var vm = ViewModel();

        var load = vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsBusy);
        Assert.False(vm.IsEmpty);
        _api.NextPendingList().SetResult([]);
        await load;
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task WithoutActiveEmergenciesTheHomeShowsTheEmptyState()
    {
        var vm = ViewModel();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasEmergencies);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task TheListShowsTheNnCaseTheIdentifiedCaseAndTheTransferBadge()
    {
        var unidentified = Active(status: "Created");
        var identified = Active(code: OtherNnCode, patient: "Ana Pérez");
        var onTheWay = Active(status: "Transferred", transfer: TransferStatus.InTransit);
        _api.Active.AddRange([unidentified, identified, onTheWay]);
        var vm = ViewModel();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal([unidentified.Id, identified.Id, onTheWay.Id], vm.Emergencies.Select(e => e.Id));
        Assert.False(vm.IsEmpty);
        Assert.Equal((NnCode, (string?)null, "Abierta", false), (vm.Emergencies[0].Identity.Primary, vm.Emergencies[0].Identity.Secondary,
            vm.Emergencies[0].StatusText, vm.Emergencies[0].HasTransfer));
        Assert.Equal(("Ana Pérez", (string?)OtherNnCode), (vm.Emergencies[1].Identity.Primary, vm.Emergencies[1].Identity.Secondary));
        Assert.Equal(("Rumbo al hospital", (string?)"Traslado en curso"), (vm.Emergencies[2].StatusText, vm.Emergencies[2].TransferText));
        Assert.StartsWith("Inicio ", vm.Emergencies[0].StartedText);
        Assert.Equal("Politrauma", vm.Emergencies[0].ChiefComplaint);
    }

    [Fact]
    public async Task AFailedLoadShowsTheErrorWithoutStaleDataAndRetryLoadsAgain()
    {
        _api.Active.Add(Active());
        var vm = ViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        _api.NextListFailure = new MobileApiException(MobileApiError.Network);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);
        Assert.Empty(vm.Emergencies);
        Assert.False(vm.IsEmpty);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Single(vm.Emergencies);
    }

    [Fact]
    public async Task AServerFailureShowsTheListMessage()
    {
        _api.NextListFailure = new MobileApiException(MobileApiError.Server, HttpStatusCode.InternalServerError);
        var vm = ViewModel();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(ParamedicMessages.ActiveEmergenciesFailed, vm.ErrorMessage);
    }

    // OnAppearing (also on returning from a case) loads again: the list reflects the server, e.g. a case just identified.
    [Fact]
    public async Task EachLoadReflectsTheCurrentServerState()
    {
        var item = Active();
        _api.Active.Add(item);
        var vm = ViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(NnCode, Assert.Single(vm.Emergencies).Identity.Primary);

        _api.Active[0] = Active(patient: "Ana Pérez", id: item.Id);
        await vm.LoadCommand.ExecuteAsync(null);

        var identity = Assert.Single(vm.Emergencies).Identity;
        Assert.Equal(("Ana Pérez", (string?)NnCode), (identity.Primary, identity.Secondary));
        Assert.Equal(2, _api.ListCalls);
    }

    [Fact]
    public async Task NewEmergencyOpensTheCreationForm()
    {
        var vm = ViewModel();

        await vm.NewEmergencyCommand.ExecuteAsync(null);

        var (route, parameters) = Assert.Single(_navigator.Navigations);
        Assert.Equal((ParamedicRoutes.NewEmergency, (IDictionary<string, object>?)null), (route, parameters));
    }

    [Fact]
    public async Task AnUnidentifiedCaseResumesOnIdentification()
    {
        var item = Active();
        _api.Active.Add(item);
        OnServer(item.Id);
        var vm = ViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.OpenCommand.ExecuteAsync(vm.Emergencies[0]);

        var (route, parameters) = Assert.Single(_navigator.Navigations);
        Assert.Equal(ParamedicRoutes.IdentifyPatient, route);
        Assert.Equal(item.Id, parameters!["EmergencyId"]);
    }

    // The route comes from the emergency read now, not from the list: identified meanwhile → vital signs.
    [Fact]
    public async Task AnIdentifiedCaseResumesOnVitalSignsAccordingToTheServerNotTheList()
    {
        var item = Active();
        _api.Active.Add(item);
        var vm = ViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        OnServer(item.Id, patient: "Ana Pérez");

        await vm.OpenCommand.ExecuteAsync(vm.Emergencies[0]);

        Assert.Equal([item.Id], _api.Reads);
        var (route, parameters) = Assert.Single(_navigator.Navigations);
        Assert.Equal(ParamedicRoutes.VitalSigns, route);
        Assert.Equal(item.Id, parameters!["EmergencyId"]);
    }

    [Theory]
    [InlineData(TransferStatus.Requested, null)]
    [InlineData(TransferStatus.InTransit, "Ana Pérez")]
    public async Task AnActiveTransferResumesOnTheTransferScreen(TransferStatus transfer, string? patient)
    {
        var item = Active(transfer: transfer, patient: patient);
        _api.Active.Add(item);
        OnServer(item.Id, patient: patient, transfer: transfer);
        var vm = ViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.OpenCommand.ExecuteAsync(vm.Emergencies[0]);

        var (route, parameters) = Assert.Single(_navigator.Navigations);
        Assert.Equal(ParamedicRoutes.Transfer, route);
        Assert.Equal(item.Id, parameters!["EmergencyId"]);
    }

    [Theory]
    [InlineData(TransferStatus.Cancelled)]
    [InlineData(TransferStatus.Received)]
    public void OnlyARequestedOrInTransitTransferRoutesToTheTransferScreen(TransferStatus transfer)
    {
        var emergency = new EmergencyDto { Id = Guid.NewGuid(), ActiveTransfer = new TransferDto { Status = transfer } };

        Assert.Equal(ParamedicRoutes.IdentifyPatient, ParamedicHomeViewModel.ResumeRoute(emergency));
    }

    // Handed off, closed or no longer accessible since the list was loaded: no navigation, a message and a fresh list.
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(null)]
    public async Task ACaseNoLongerActiveIsNotOpenedAndTheListIsReloaded(HttpStatusCode? status)
    {
        var item = Active();
        _api.Active.Add(item);
        var vm = ViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        if (status is { } failure)
        {
            _api.NextReadFailure = ScriptedParamedicApi.Status(failure);
        }
        else
        {
            OnServer(item.Id, status: EmergencyStatus.Closed);
        }

        _api.Active.Clear();

        await vm.OpenCommand.ExecuteAsync(vm.Emergencies[0]);

        Assert.Empty(_navigator.Navigations);
        Assert.Equal(ParamedicMessages.EmergencyNoLongerActive, vm.ErrorMessage);
        Assert.Empty(vm.Emergencies);
        Assert.Equal(2, _api.ListCalls);
    }

    [Fact]
    public async Task ANetworkFailureWhileOpeningStaysOnTheHomeWithTheList()
    {
        var item = Active();
        _api.Active.Add(item);
        var vm = ViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        _api.NextReadFailure = new MobileApiException(MobileApiError.Network);

        await vm.OpenCommand.ExecuteAsync(vm.Emergencies[0]);

        Assert.Empty(_navigator.Navigations);
        Assert.Equal(MobileApiErrorMessages.Network, vm.ErrorMessage);
        Assert.Single(vm.Emergencies);
    }

    // A slower earlier load must not overwrite the result of a later one.
    [Fact]
    public async Task ALateResponseOfAReplacedLoadIsIgnored()
    {
        _api.HoldLists = true;
        var vm = ViewModel();
        var first = vm.LoadCommand.ExecuteAsync(null);
        var firstResponse = _api.NextPendingList();
        // A second load (pull to refresh, another OnAppearing) replaces the first one.
        var secondLoad = vm.LoadCommand.ExecuteAsync(null);
        var secondResponse = _api.NextPendingList();
        var current = Active(patient: "Actual");
        secondResponse.SetResult([current]);
        await secondLoad;

        firstResponse.SetResult([Active(patient: "Antigua"), Active()]);
        await first;

        Assert.Equal([current.Id], vm.Emergencies.Select(e => e.Id));
        Assert.False(vm.IsBusy);
    }

    // The account logs out while its list is still on the way: the late answer never reaches the screen.
    [Fact]
    public async Task LoggingOutWhileTheListIsPendingDropsTheLateResponse()
    {
        _api.Active.Add(Active());
        var vm = ViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Emergencies);
        _api.HoldLists = true;
        var load = vm.LoadCommand.ExecuteAsync(null);
        var pending = _api.NextPendingList();

        _session.Clear();

        Assert.Empty(vm.Emergencies);
        Assert.False(vm.IsBusy);
        pending.SetResult([Active(), Active()]);
        await load;
        Assert.Empty(vm.Emergencies);
        Assert.Null(vm.ErrorMessage);
    }

    // Account A → account B on the same app: A's list is cleared at once and A's late answer (even an error) is dropped.
    [Fact]
    public async Task SwitchingAccountsClearsTheListAndIgnoresTheLateAnswerOfTheFirstAccount()
    {
        _api.Active.Add(Active());
        var vm = ViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        _api.HoldLists = true;
        var lateA = vm.LoadCommand.ExecuteAsync(null);
        var pendingA = _api.NextPendingList();

        ParamedicSession.SignIn(_session, Guid.NewGuid(), "otro");

        Assert.Empty(vm.Emergencies);
        Assert.Equal("otro", vm.DisplayName);
        pendingA.SetException(new MobileApiException(MobileApiError.Forbidden, HttpStatusCode.Forbidden));
        await lateA;
        Assert.Null(vm.ErrorMessage);
        Assert.Empty(vm.Emergencies);

        var loadB = vm.LoadCommand.ExecuteAsync(null);
        var ofB = Active(code: OtherNnCode);
        _api.NextPendingList().SetResult([ofB]);
        await loadB;
        Assert.Equal([ofB.Id], vm.Emergencies.Select(e => e.Id));
    }

    // Renewing the token of the same account keeps the list.
    [Fact]
    public async Task RenewingTheSameAccountKeepsTheList()
    {
        _api.Active.Add(Active());
        var vm = ViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        _session.SetAuthentication("renewed", DateTimeOffset.UtcNow.AddMinutes(15));

        Assert.Single(vm.Emergencies);
    }

    // Opening a case while the session changes: never navigates into the previous account's emergency.
    [Fact]
    public async Task ASessionChangeWhileOpeningNeverNavigates()
    {
        var item = Active();
        _api.Active.Add(item);
        var vm = ViewModel();
        await vm.LoadCommand.ExecuteAsync(null);
        _api.HoldReads = true;
        var open = vm.OpenCommand.ExecuteAsync(vm.Emergencies[0]);
        var read = _api.NextPendingRead();

        _session.Clear();
        read.SetResult(OnServer(item.Id));
        await open;

        Assert.Empty(_navigator.Navigations);
        Assert.Empty(vm.Emergencies);
    }

    [Fact]
    public async Task WithoutASessionNothingIsRequested()
    {
        _session.Clear();
        var vm = ViewModel();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(0, _api.ListCalls);
        Assert.Empty(vm.Emergencies);
        Assert.False(vm.IsEmpty);
    }
}

// Identidad de la emergencia (cabecera y lista): NN como principal sin identificar; nombre con el NN como trazabilidad.
public class EmergencyIdentityTests
{
    private const string NnCode = "NN-0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public void AnUnidentifiedCaseShowsTheNnCodeAsPrimary()
    {
        var identity = EmergencyIdentity.From(NnCode, null);

        Assert.Equal((NnCode, (string?)null, false, false), (identity.Primary, identity.Secondary, identity.HasSecondary, identity.IsIdentified));
    }

    [Fact]
    public void AnIdentifiedCaseShowsTheNameAndKeepsTheNnCodeAsSecondary()
    {
        var identity = EmergencyIdentity.From(new EmergencyDto
        {
            TemporaryPatientCode = NnCode,
            Patient = new PatientSummaryDto { Id = Guid.NewGuid(), DisplayName = " Ana Pérez " }
        });

        Assert.Equal(("Ana Pérez", (string?)NnCode, true, true), (identity.Primary, identity.Secondary, identity.HasSecondary, identity.IsIdentified));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NN-123")]
    [InlineData("XX-0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("NN-0123456789ABCDEF0123456789ABCDEZ")]
    public void AMissingOrInvalidCodeIsNeverShownAsIs(string? code)
    {
        Assert.Equal(EmergencyIdentity.UnknownNnCode, EmergencyIdentity.From(code, null).Primary);
        var identified = EmergencyIdentity.From(code, "Ana Pérez");
        Assert.Equal(("Ana Pérez", false), (identified.Primary, identified.HasSecondary));
    }

    [Fact]
    public void ALowercaseCodeIsNormalized() =>
        Assert.Equal(NnCode, EmergencyIdentity.From(NnCode.ToLowerInvariant(), null).Primary);

    [Fact]
    public void AnIdentifiedPatientWithoutANameStillKeepsTheTrace()
    {
        var identity = EmergencyIdentity.From(NnCode, "  ");

        Assert.Equal((EmergencyIdentity.IdentifiedWithoutName, (string?)NnCode), (identity.Primary, identity.Secondary));
    }
}
