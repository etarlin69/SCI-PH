using SCI_PH.Mobile.Features.Patient.ViewModels;

namespace SCI_PH.Mobile.Features.Patient.Ai;

public partial class AiAssistantPage : ContentPage
{
    public AiAssistantPage(AiAssistantViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
