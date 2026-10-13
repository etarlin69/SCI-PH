using SCI_PH.Mobile.Features.Paramedic;

namespace SCI_PH.Mobile;

/// <summary>
/// Shell único de SCI-PH Mobile. Cada feature declara su raíz en el XAML y registra sus propias rutas aquí
/// mediante su extensión; el Shell no conoce las páginas internas de ninguna feature.
/// </summary>
public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        ParamedicServiceCollectionExtensions.RegisterParamedicRoutes();
        // Patient (PR #18): PatientServiceCollectionExtensions.RegisterPatientRoutes();
    }
}
