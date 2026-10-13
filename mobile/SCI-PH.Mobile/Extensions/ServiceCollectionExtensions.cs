using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCI_PH.Mobile.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddCoreServices(this IServiceCollection services)
        {
            // Aquí irán servicios compartidos: HttpClient del Mobile BFF,
            // manejo de errores, almacenamiento seguro, etc.
            return services;
        }
    }
}
