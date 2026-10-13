using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Features.Patient;
using SCI_PH.Mobile.Features.Patient.Services.Mocks;
using SCI_PH.Mobile.Features.Patient.ViewModels;

namespace SCI_PH.Mobile.Tests.Patient;

// ViewModels y servicios del paciente con los mocks del prototipo y navegación/diálogos falsos: sin Shell ni UI.
public class PatientFeatureTests
{
    [Fact]
    public async Task HomeLoadsTheSummaryFromTheDataService()
    {
        var vm = new HomeViewModel(new MockPatientDataService(), new FakeNavigator());

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasError);
        Assert.NotNull(vm.Summary);
        Assert.Equal("Paciente Demo", vm.Summary.DisplayName);
        Assert.NotNull(vm.Summary.LatestResult);
    }

    [Fact]
    public async Task AnEmptyQuestionIsNotSent()
    {
        var vm = new AiAssistantViewModel(new MockPatientAssistantService()) { Question = "   " };

        Assert.False(vm.SendCommand.CanExecute(null));
        await vm.SendCommand.ExecuteAsync(null);

        // Solo el saludo inicial.
        var greeting = Assert.Single(vm.Messages);
        Assert.False(greeting.IsFromPatient);
    }

    [Fact]
    public async Task AQuestionAddsThePatientMessageAndTheAssistantAnswer()
    {
        var vm = new AiAssistantViewModel(new MockPatientAssistantService()) { Question = "¿Qué es la hemoglobina?" };

        Assert.True(vm.SendCommand.CanExecute(null));
        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Messages.Count);
        Assert.True(vm.Messages[1].IsFromPatient);
        Assert.Equal("¿Qué es la hemoglobina?", vm.Messages[1].Text);
        Assert.False(vm.Messages[2].IsFromPatient);
        Assert.Contains("hemoglobina", vm.Messages[2].Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrEmpty(vm.Question));
    }

    [Fact]
    public async Task TheSessionCanBeCreatedReadAndCleared()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        var session = new MockPatientSessionService(clock);
        var changes = 0;
        session.SessionChanged += (_, _) => changes++;

        Assert.False(session.IsAuthenticated);

        var created = await session.CreateSessionAsync();

        Assert.True(session.IsAuthenticated);
        Assert.Same(created, session.CurrentSession);
        Assert.Equal("Paciente Demo", session.CurrentSession!.PatientName);

        // La sesión simulada caduca a la hora.
        clock.Now = clock.Now.AddHours(1);
        Assert.False(session.IsAuthenticated);

        await session.ClearSessionAsync();

        Assert.Null(session.CurrentSession);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task TheQrAloneDoesNotAuthenticateTheFaceStepCreatesTheSession()
    {
        var session = new MockPatientSessionService(TimeProvider.System);
        var navigator = new FakeNavigator();
        var dialogs = new FakeDialogs();

        await new QrAccessViewModel(navigator).SimulateQrCommand.ExecuteAsync(null);

        Assert.False(session.IsAuthenticated);
        Assert.Equal(PatientRoutes.FaceVerify, navigator.Navigations[^1].Route);

        await new FaceVerifyViewModel(session, navigator, dialogs).VerifyCommand.ExecuteAsync(null);

        Assert.True(session.IsAuthenticated);
        Assert.Equal(AppRoutes.Root(PatientRoutes.Profile), navigator.Navigations[^1].Route);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
