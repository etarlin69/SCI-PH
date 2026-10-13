using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Features.Paramedic.Views;

public partial class IdentifyPatientPage : ContentPage
{
    public IdentifyPatientPage(IdentifyPatientViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
