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

    /// <summary>
    /// Registra las páginas del flujo que no están declaradas en el XAML del Shell (se apilan sobre la actual).
    /// La raíz, <see cref="ParamedicRoutes.NewEmergency"/>, la declara el AppShell.
    /// </summary>
    public static void RegisterParamedicRoutes()
    {
        Routing.RegisterRoute(ParamedicRoutes.IdentifyPatient, typeof(IdentifyPatientPage));
        Routing.RegisterRoute(ParamedicRoutes.VitalSigns, typeof(VitalSignsPage));
        Routing.RegisterRoute(ParamedicRoutes.Procedures, typeof(ProceduresPage));
        Routing.RegisterRoute(ParamedicRoutes.Medications, typeof(MedicationsPage));
    }
}
