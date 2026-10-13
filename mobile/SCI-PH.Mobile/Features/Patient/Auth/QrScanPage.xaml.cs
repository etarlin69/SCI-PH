using SCI_PH.Mobile.Features.Patient.ViewModels;

namespace SCI_PH.Mobile.Features.Patient.Auth;

public partial class QrScanPage : ContentPage
{
    public QrScanPage(QrAccessViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
