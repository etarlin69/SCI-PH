using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SCI_PH.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            // Punto central para registrar las implementaciones de las abstracciones
            // definidas en Application (Application/Abstractions):
            // - Azure Blob Storage (IStorageService)
            // - Verificación facial (IFaceVerificationService)
            // - OCR (IOcrService)
            // - QR (IQrService)
            // - IA (IAiAssistantService)
            // - Otros servicios externos

            return services;
        }
    }
}
