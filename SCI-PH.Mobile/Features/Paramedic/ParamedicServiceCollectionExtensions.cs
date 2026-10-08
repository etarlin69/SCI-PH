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
        services.AddSingleton<IParamedicCatalogService, MockParamedicCatalogService>();

        // ViewModels
        services.AddTransient<NewEmergencyViewModel>();
        services.AddTransient<IdentifyPatientViewModel>();
        services.AddTransient<VitalSignsViewModel>();
        services.AddTransient<ProceduresViewModel>();
        services.AddTransient<MedicationsViewModel>();

        // Views
        services.AddTransient<NewEmergencyPage>();
        services.AddTransient<IdentifyPatientPage>();
        services.AddTransient<VitalSignsPage>();
        services.AddTransient<ProceduresPage>();
        services.AddTransient<MedicationsPage>();

        return services;
    }
}
