using SCI_PH.Mobile.Features.Patient.ViewModels;

namespace SCI_PH.Mobile.Features.Patient.History;

public partial class EncounterDetailPage : ContentPage
{
    private readonly EncounterDetailViewModel _viewModel;

    public EncounterDetailPage(EncounterDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
