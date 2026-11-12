using SCI_PH.Mobile.Features.Auth.ViewModels;

namespace SCI_PH.Mobile.Features.Auth.Pages;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
