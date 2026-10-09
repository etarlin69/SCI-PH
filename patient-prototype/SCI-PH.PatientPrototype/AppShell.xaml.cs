using SCI_PH.PatientPrototype.Features.Patient.Auth;
using SCI_PH.PatientPrototype.Features.Patient.Ai;

namespace SCI_PH.PatientPrototype;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(
            nameof(FaceVerifyPage),
            typeof(FaceVerifyPage));

        Routing.RegisterRoute(
            nameof(AiAssistantPage),
            typeof(AiAssistantPage));
    }
}