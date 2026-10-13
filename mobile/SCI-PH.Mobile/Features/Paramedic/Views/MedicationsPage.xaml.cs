using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Features.Paramedic.Views;

public partial class MedicationsPage : ContentPage
{
    private readonly MedicationsViewModel _viewModel;

    public MedicationsPage(MedicationsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCatalogCommand.ExecuteAsync(null);
    }
}
