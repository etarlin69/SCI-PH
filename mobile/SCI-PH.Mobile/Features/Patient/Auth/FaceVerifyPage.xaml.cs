using SCI_PH.Mobile.Features.Patient.ViewModels;

namespace SCI_PH.Mobile.Features.Patient.Auth;

public partial class FaceVerifyPage : ContentPage
{
    public FaceVerifyPage(FaceVerifyViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
