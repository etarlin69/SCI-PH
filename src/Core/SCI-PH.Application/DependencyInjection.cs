using Microsoft.Extensions.DependencyInjection;

namespace SCI_PH.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            // Punto central para registrar servicios de la capa Application
            // (casos de uso, validadores, comportamientos, etc.).

            return services;
        }
    }
}
