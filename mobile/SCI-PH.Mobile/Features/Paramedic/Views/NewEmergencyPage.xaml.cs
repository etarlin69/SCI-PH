using SCI_PH.Mobile.Features.Paramedic.ViewModels;

namespace SCI_PH.Mobile.Features.Paramedic.Views;

/// <summary>Formulario de una emergencia nueva, sobre el inicio del paramédico (el cierre de sesión está en el inicio).</summary>
public partial class NewEmergencyPage : ContentPage
{
    public NewEmergencyPage(NewEmergencyViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
