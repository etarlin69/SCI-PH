using SCI_PH.Mobile.App.ViewModels;

namespace SCI_PH.Mobile.App.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}