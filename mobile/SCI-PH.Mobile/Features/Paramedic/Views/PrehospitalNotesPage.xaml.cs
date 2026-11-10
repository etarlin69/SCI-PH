using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Features.Paramedic.Views;

public partial class PrehospitalNotesPage : ContentPage
{
    private readonly PrehospitalNotesViewModel _viewModel;

    public PrehospitalNotesPage(PrehospitalNotesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    // Cabecera de la emergencia (código NN o paciente) desde el servidor, cada vez que la página se ve.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.RefreshIdentityAsync();
    }
}
