using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Features.Paramedic.Views;

public partial class IdentifyPatientPage : ContentPage
{
    private readonly IdentifyPatientViewModel _viewModel;

    public IdentifyPatientPage(IdentifyPatientViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    // La lectura de huella se consulta solo mientras la página se ve: nunca queda una consulta huérfana.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.ResumeFingerprintPolling();
    }

    protected override void OnDisappearing()
    {
        _viewModel.StopFingerprintPolling();
        base.OnDisappearing();
    }
}
