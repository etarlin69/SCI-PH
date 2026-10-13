using SCI_PH.PatientPrototype.Features.Patient.ViewModels;

namespace SCI_PH.PatientPrototype.Features.Patient.Documents;

public partial class DocumentsPage : ContentPage
{
    private readonly DocumentsViewModel _viewModel;

    public DocumentsPage(DocumentsViewModel viewModel)
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
