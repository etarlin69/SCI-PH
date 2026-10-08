using SCI_PH.Mobile.Features.Paramedic;
using SCI_PH.Mobile.Features.Paramedic.Views;

namespace SCI_PH.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Páginas que no están declaradas en el XAML del Shell.
        Routing.RegisterRoute(ParamedicRoutes.IdentifyPatient, typeof(IdentifyPatientPage));
        Routing.RegisterRoute(ParamedicRoutes.VitalSigns, typeof(VitalSignsPage));
    }
}
