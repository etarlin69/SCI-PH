using SCI_PH.Mobile.Features.Auth.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Features.Paramedic.Views;

public partial class ParamedicHomePage : ContentPage
{
    private readonly ParamedicHomeViewModel _viewModel;

    public ParamedicHomePage(ParamedicHomeViewModel viewModel, ClinicalAccountViewModel account)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        LogoutToolbarItem.Command = account.LogoutCommand;
        AccountSecurityToolbarItem.Command = account.OpenAccountSecurityCommand;
    }

    // Cada vez que se ve (también al volver de una emergencia) la lista se pide de nuevo al servidor.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
