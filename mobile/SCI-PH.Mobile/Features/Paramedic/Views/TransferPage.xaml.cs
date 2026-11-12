using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Features.Paramedic.Views;

public partial class TransferPage : ContentPage
{
    private readonly TransferViewModel _viewModel;

    public TransferPage(TransferViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    // Each time the page appears (also when returning from a clinical screen) the state comes from the server.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
