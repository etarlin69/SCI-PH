using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Features.Paramedic.Views;

public partial class VitalSignsPage : ContentPage
{
    public VitalSignsPage(VitalSignsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
