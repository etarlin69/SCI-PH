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
}
