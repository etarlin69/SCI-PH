using Android.App;
using Android.Runtime;

namespace SCI_PH.Mobile
{
    // En Debug, HTTP sin cifrar solo hacia el Mobile BFF local (Platforms/Android/DebugResources). Release no declara
    // network security config y bloquea HTTP de forma explícita (usesCleartextTraffic="false"), también en Android 6-8,
    // donde el bloqueo por defecto de Android 9 no aplica. TLS se valida siempre.
#if DEBUG
    [Application(NetworkSecurityConfig = "@xml/network_security_config")]
#else
    [Application(UsesCleartextTraffic = false)]
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
