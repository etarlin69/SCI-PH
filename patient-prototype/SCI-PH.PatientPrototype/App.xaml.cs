using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

namespace SCI_PH.PatientPrototype;

public partial class App : Application
{
    private readonly IPatientSessionService _session;

    public App(IPatientSessionService session)
    {
        InitializeComponent();
        _session = session;
    }

    // El Shell se crea aquí y no por DI en el constructor: necesita los recursos de App.xaml ya cargados.
    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell(_session));
    }
}
