using SCI_PH.Mobile.Common.Authentication;

namespace SCI_PH.Mobile
{
    public partial class App : Application
    {
        private readonly IClinicalSessionService _session;
        private readonly IMobileAuthService _auth;

        public App(IClinicalSessionService session, IMobileAuthService auth)
        {
            InitializeComponent();
            _session = session;
            _auth = auth;
        }

        // El Shell se crea aquí, no en el constructor: necesita los recursos de App.xaml ya cargados.
        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell(_session, _auth));
        }
    }
}
