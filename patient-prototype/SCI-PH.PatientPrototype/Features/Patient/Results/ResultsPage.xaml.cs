using SCI_PH.PatientPrototype.Features.Patient.ViewModels;

namespace SCI_PH.PatientPrototype.Features.Patient.Results;

public partial class ResultsPage : ContentPage
{
    private readonly ResultsViewModel _viewModel;

    public ResultsPage(ResultsViewModel viewModel)
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
