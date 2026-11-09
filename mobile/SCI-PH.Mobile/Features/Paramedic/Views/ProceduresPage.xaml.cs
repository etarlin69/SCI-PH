using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Features.Paramedic.Views;

public partial class ProceduresPage : ContentPage
{
    private readonly ProceduresViewModel _viewModel;

    public ProceduresPage(ProceduresViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Cabecera de la emergencia (código NN o paciente) desde el servidor; no bloquea el catálogo.
        _ = _viewModel.RefreshIdentityAsync();
        await _viewModel.LoadCatalogCommand.ExecuteAsync(null);
    }
}
