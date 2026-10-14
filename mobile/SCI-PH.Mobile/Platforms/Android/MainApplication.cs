using Android.App;
using Android.Runtime;

namespace SCI_PH.Mobile
{
    // En Debug, HTTP sin cifrar solo hacia el Mobile BFF local (Platforms/Android/DebugResources). Release no declara
    // network security config: el tráfico HTTP queda bloqueado y TLS se valida siempre.
#if DEBUG
    [Application(NetworkSecurityConfig = "@xml/network_security_config")]
#else
    [Application]
#endif
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
