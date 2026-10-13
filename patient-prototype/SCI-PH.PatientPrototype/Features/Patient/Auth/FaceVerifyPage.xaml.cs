using SCI_PH.PatientPrototype.Features.Patient.ViewModels;

namespace SCI_PH.PatientPrototype.Features.Patient.Auth;

public partial class FaceVerifyPage : ContentPage
{
    public FaceVerifyPage(FaceVerifyViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
