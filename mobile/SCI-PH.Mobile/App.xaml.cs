using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;

namespace SCI_PH.Mobile
{
    public partial class App : Application
    {
        private readonly IPatientSessionService _patientSession;
        private readonly IMobileAuthService _clinicalAuth;

        public App(IPatientSessionService patientSession, IMobileAuthService clinicalAuth)
        {
            InitializeComponent();
            _patientSession = patientSession;
            _clinicalAuth = clinicalAuth;
        }

        // El Shell se crea aquí, no en el constructor: necesita los recursos de App.xaml ya cargados.
        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell(_patientSession, _clinicalAuth));
        }
    }
}
