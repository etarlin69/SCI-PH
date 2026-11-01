using Microsoft.Extensions.DependencyInjection.Extensions;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.Dialogs;
using SCI_PH.Mobile.Common.Entry;
using SCI_PH.Mobile.Common.Location;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.Startup;

namespace SCI_PH.Mobile.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Servicios compartidos por todas las features (Auth, Paramedic, Patient): navegación, diálogos, ubicación,
        /// cliente del Mobile BFF y sesión del personal clínico.
        /// </summary>
        public static IServiceCollection AddCoreServices(this IServiceCollection services)
        {
            services.AddSingleton<IAppNavigator, ShellAppNavigator>();
            services.AddSingleton<IAppDialogService, ShellAppDialogService>();
            services.AddSingleton(Geolocation.Default);
            services.AddSingleton<IDeviceLocationService, MauiDeviceLocationService>();
            services.TryAddSingleton(TimeProvider.System);

            AddMobileBff(services);

            // Arranque (restaura la sesión guardada) y entrada compartida (un solo inicio de sesión).
            services.AddTransient<StartupViewModel>();
            services.AddTransient<StartupPage>();
            services.AddTransient<EntryViewModel>();
            services.AddTransient<EntryPage>();
            return services;
        }

        // Sesión del personal clínico: access token en memoria, refresh token en SecureStorage.
        private static void AddMobileBff(IServiceCollection services)
        {
            var options = MobileApiOptions.ForCurrentBuild();
            services.AddSingleton(options);
            services.AddSingleton<IClinicalSessionService, ClinicalSessionService>();
            services.AddSingleton<ISecureTokenStore>(_ => new MauiSecureTokenStore(SecureStorage.Default));

            // Cliente público (login, MFA, refresh, logout, /me con el token recién emitido): sin el handler de
            // autenticación, así el refresh nunca puede dispararse a sí mismo.
            services.AddSingleton<IMobileAuthApi>(_ =>
                new MobileAuthApi(new MobileApiClient(MobileApiClient.CreateHttpClient(options))));
            services.AddSingleton<MobileAuthService>();
            services.AddSingleton<IMobileAuthService>(sp => sp.GetRequiredService<MobileAuthService>());
            services.AddSingleton<IClinicalSessionRefresher>(sp => sp.GetRequiredService<MobileAuthService>());

            // Cliente de los endpoints protegidos de las features clínicas: Bearer + refresh single-flight ante un 401.
            services.AddSingleton<IMobileApiClient>(sp => new MobileApiClient(MobileApiClient.CreateHttpClient(
                options,
                new AuthenticatedHttpHandler(
                    sp.GetRequiredService<IClinicalSessionService>(),
                    sp.GetRequiredService<IClinicalSessionRefresher>()))));
        }
    }
}
