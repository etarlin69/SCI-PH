using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Features.Paramedic.Views;

public partial class PrehospitalNotesPage : ContentPage
{
    public PrehospitalNotesPage(PrehospitalNotesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
