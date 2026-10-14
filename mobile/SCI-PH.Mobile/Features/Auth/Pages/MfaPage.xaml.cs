using SCI_PH.Mobile.Features.Auth.ViewModels;

namespace SCI_PH.Mobile.Features.Auth.Pages;

public partial class MfaPage : ContentPage
{
    public MfaPage(MfaViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
