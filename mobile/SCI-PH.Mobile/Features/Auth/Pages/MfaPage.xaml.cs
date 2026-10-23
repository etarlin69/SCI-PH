using SCI_PH.Mobile.Features.Auth.ViewModels;

namespace SCI_PH.Mobile.Features.Auth.Pages;

public partial class MfaPage : ContentPage
{
    private readonly MfaViewModel _viewModel;

    public MfaPage(MfaViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    // También al volver con la flecha de la barra de título o el botón atrás del sistema, no solo con "Volver al login".
    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        _viewModel.ForgetChallenge();
    }
}
