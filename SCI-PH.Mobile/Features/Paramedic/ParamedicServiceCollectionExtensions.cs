using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.Views;

namespace SCI_PH.Mobile.Features.Paramedic;

public static class ParamedicServiceCollectionExtensions
{
    public static IServiceCollection AddParamedicFeature(this IServiceCollection services)
    {
        // Servicios (por ahora siempre mocks)
        services.AddSingleton<IParamedicApiService, MockParamedicApiService>();
        services.AddSingleton<IFingerprintReaderService, MockFingerprintReaderService>();

        // ViewModels
        services.AddTransient<NewEmergencyViewModel>();
        services.AddTransient<IdentifyPatientViewModel>();

        // Views
        services.AddTransient<NewEmergencyPage>();
        services.AddTransient<IdentifyPatientPage>();

        return services;
    }
}
