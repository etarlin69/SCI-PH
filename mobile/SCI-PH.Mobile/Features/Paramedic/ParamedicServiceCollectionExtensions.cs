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
        // Emergencia, identificación por documento, por huella y con el QR del paciente, signos vitales, procedimientos, medicamentos,
        // observaciones, traslado y sus catálogos: Mobile BFF real.
        services.AddSingleton<MockParamedicApiService>();
        services.AddSingleton<IParamedicApiService, HttpParamedicApiService>();
        services.AddSingleton<IFingerprintIdentificationService, HttpFingerprintIdentificationService>();
        services.AddSingleton<IParamedicCatalogService, HttpParamedicCatalogService>();

        // Identificación con el QR del paciente: el permiso de cámara solo lo pide el escáner del paramédico.
        services.AddSingleton<ICameraPermissionService, MauiCameraPermissionService>();

        // ViewModels
        services.AddTransient<ParamedicHomeViewModel>();
        services.AddTransient<NewEmergencyViewModel>();
        services.AddTransient<IdentifyPatientViewModel>();
        services.AddTransient<QrScannerViewModel>();
        services.AddTransient<VitalSignsViewModel>();
        services.AddTransient<ProceduresViewModel>();
        services.AddTransient<MedicationsViewModel>();
        services.AddTransient<PrehospitalNotesViewModel>();
        services.AddTransient<TransferViewModel>();

        // Views
        services.AddTransient<ParamedicHomePage>();
        services.AddTransient<NewEmergencyPage>();
        services.AddTransient<IdentifyPatientPage>();
        services.AddTransient<QrScannerPage>();
        services.AddTransient<VitalSignsPage>();
        services.AddTransient<ProceduresPage>();
        services.AddTransient<MedicationsPage>();
        services.AddTransient<PrehospitalNotesPage>();
        services.AddTransient<TransferPage>();

        return services;
    }

    /// <summary>
    /// Registra las páginas del flujo: todas se apilan sobre el inicio del paramédico (<see cref="ParamedicRoutes.Home"/>,
    /// un ShellContent de AppShell, que no se registra aquí).
    /// </summary>
    public static void RegisterParamedicRoutes()
    {
        Routing.RegisterRoute(ParamedicRoutes.NewEmergency, typeof(NewEmergencyPage));
        Routing.RegisterRoute(ParamedicRoutes.IdentifyPatient, typeof(IdentifyPatientPage));
        Routing.RegisterRoute(ParamedicRoutes.ScanQr, typeof(QrScannerPage));
        Routing.RegisterRoute(ParamedicRoutes.VitalSigns, typeof(VitalSignsPage));
        Routing.RegisterRoute(ParamedicRoutes.Procedures, typeof(ProceduresPage));
        Routing.RegisterRoute(ParamedicRoutes.Medications, typeof(MedicationsPage));
        Routing.RegisterRoute(ParamedicRoutes.Notes, typeof(PrehospitalNotesPage));
        Routing.RegisterRoute(ParamedicRoutes.Transfer, typeof(TransferPage));
    }
}
