using SCI_PH.Mobile.Features.Paramedic.Services;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;
using SCI_PH.Mobile.Features.Paramedic.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.Views;

namespace SCI_PH.Mobile.Features.Paramedic;

public static class ParamedicServiceCollectionExtensions
{
    public static IServiceCollection AddParamedicFeature(this IServiceCollection services)
    {
        // Emergencia, identificación por documento, signos vitales, procedimientos y su catálogo: Mobile BFF real.
        // Huella, medicamentos y su catálogo: todavía mocks.
        services.AddSingleton<MockParamedicApiService>();
        services.AddSingleton<IParamedicApiService, HttpParamedicApiService>();
        services.AddSingleton<IFingerprintReaderService, MockFingerprintReaderService>();
        services.AddSingleton<MockParamedicCatalogService>();
        services.AddSingleton<IParamedicCatalogService, HttpParamedicCatalogService>();

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

    /// <summary>
    /// Registra las páginas del flujo: todas se apilan, empezando por <see cref="ParamedicRoutes.NewEmergency"/> sobre la
    /// entrada compartida.
    /// </summary>
    public static void RegisterParamedicRoutes()
    {
        Routing.RegisterRoute(ParamedicRoutes.NewEmergency, typeof(NewEmergencyPage));
        Routing.RegisterRoute(ParamedicRoutes.IdentifyPatient, typeof(IdentifyPatientPage));
        Routing.RegisterRoute(ParamedicRoutes.VitalSigns, typeof(VitalSignsPage));
        Routing.RegisterRoute(ParamedicRoutes.Procedures, typeof(ProceduresPage));
        Routing.RegisterRoute(ParamedicRoutes.Medications, typeof(MedicationsPage));
    }
}
