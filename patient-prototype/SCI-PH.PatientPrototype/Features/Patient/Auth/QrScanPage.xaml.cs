using SCI_PH.PatientPrototype.Features.Patient.ViewModels;

namespace SCI_PH.PatientPrototype.Features.Patient.Auth;

public partial class QrScanPage : ContentPage
{
    public QrScanPage(QrAccessViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
