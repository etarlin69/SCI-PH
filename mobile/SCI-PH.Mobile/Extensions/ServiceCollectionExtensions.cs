using SCI_PH.Mobile.Common.Dialogs;
using SCI_PH.Mobile.Common.Navigation;

namespace SCI_PH.Mobile.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Servicios compartidos por todas las features (Paramedic, Patient). Aquí irán también el HttpClient del
        /// Mobile BFF, el manejo de errores y el almacenamiento seguro.
        /// </summary>
        public static IServiceCollection AddCoreServices(this IServiceCollection services)
        {
            services.AddSingleton<IAppNavigator, ShellAppNavigator>();
            services.AddSingleton<IAppDialogService, ShellAppDialogService>();
            return services;
        }
    }
}
