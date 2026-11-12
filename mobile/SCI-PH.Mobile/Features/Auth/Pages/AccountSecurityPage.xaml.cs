using SCI_PH.Mobile.Features.Auth.ViewModels;

namespace SCI_PH.Mobile.Features.Auth.Pages;

public partial class AccountSecurityPage : ContentPage
{
    private readonly AccountSecurityViewModel _viewModel;

    public AccountSecurityPage(AccountSecurityViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    // Nada del formulario sobrevive a la pantalla: las contraseñas escritas se borran al salir.
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.CurrentPassword = string.Empty;
        _viewModel.NewPassword = string.Empty;
        _viewModel.ConfirmPassword = string.Empty;
        _viewModel.ErrorMessage = null;
    }
}
