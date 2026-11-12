using SCI_PH.Mobile.Features.Patient.Documents;
using SCI_PH.Mobile.Features.Patient.History;
using SCI_PH.Mobile.Features.Patient.Home;
using SCI_PH.Mobile.Features.Patient.Profile;
using SCI_PH.Mobile.Features.Patient.Qr;
using SCI_PH.Mobile.Features.Patient.Results;
using SCI_PH.Mobile.Features.Patient.Services;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;
using SCI_PH.Mobile.Features.Patient.ViewModels;

namespace SCI_PH.Mobile.Features.Patient;

public static class PatientServiceCollectionExtensions
{
    /// <summary>
    /// Registra el módulo del paciente. Su sesión es la de la cuenta (inicio de sesión común, ADR-015): no hay sesión
    /// propia del paciente ni acceso por QR o rostro ("Mi QR" solo lo identifica ante el personal). Los datos son los REALES de su expediente
    /// (<c>/api/mobile/patient/me</c>) con el cliente protegido común; Documentos es una pantalla de "todavía no
    /// disponible" y el asistente de IA no está en la app.
    /// </summary>
    public static IServiceCollection AddPatientFeature(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPatientDataService, HttpPatientDataService>();
        services.AddSingleton<IPatientQrService, HttpPatientQrService>();

        // Navegación y diálogos son los comunes de la app (AddCoreServices).

        services.AddTransient<HomeViewModel>();
        services.AddTransient<ProfileViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<ResultsViewModel>();
        services.AddTransient<MyQrViewModel>();
        services.AddTransient<EncounterDetailViewModel>();

        services.AddTransient<HomePage>();
        services.AddTransient<ProfilePage>();
        services.AddTransient<HistoryPage>();
        services.AddTransient<ResultsPage>();
        services.AddTransient<MyQrPage>();
        services.AddTransient<DocumentsPage>();
        services.AddTransient<EncounterDetailPage>();

        return services;
    }

    /// <summary>
    /// Rutas apiladas del área del paciente (no son secciones del menú). Se registran una vez, al crear el Shell.
    /// </summary>
    public static void RegisterPatientRoutes()
    {
        Routing.RegisterRoute(PatientRoutes.EncounterDetail, typeof(EncounterDetailPage));
    }
}
