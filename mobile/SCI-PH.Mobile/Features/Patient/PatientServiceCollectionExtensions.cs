using SCI_PH.Mobile.Features.Patient.Ai;
using SCI_PH.Mobile.Features.Patient.Auth;
using SCI_PH.Mobile.Features.Patient.Documents;
using SCI_PH.Mobile.Features.Patient.History;
using SCI_PH.Mobile.Features.Patient.Home;
using SCI_PH.Mobile.Features.Patient.Profile;
using SCI_PH.Mobile.Features.Patient.Results;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;
using SCI_PH.Mobile.Features.Patient.Services.Mocks;
using SCI_PH.Mobile.Features.Patient.ViewModels;

namespace SCI_PH.Mobile.Features.Patient;

public static class PatientServiceCollectionExtensions
{
    /// <summary>
    /// Registra el módulo del paciente. Los servicios son mocks del prototipo: al conectar el Mobile BFF solo cambian
    /// estas líneas, no las páginas ni los ViewModels.
    /// </summary>
    public static IServiceCollection AddPatientFeature(this IServiceCollection services)
    {
        // Sesión única en memoria mientras la app está abierta.
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPatientSessionService, MockPatientSessionService>();
        services.AddSingleton<IPatientDataService, MockPatientDataService>();
        services.AddSingleton<IPatientAssistantService, MockPatientAssistantService>();

        // Navegación y diálogos son los comunes de la app (AddCoreServices).
        // Perfil: demostración local de foto y descripción.
        services.AddSingleton(Preferences.Default);
        services.AddSingleton(MediaPicker.Default);
        services.AddSingleton(FileSystem.Current);

        services.AddTransient<QrAccessViewModel>();
        services.AddTransient<FaceVerifyViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<ProfileViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<ResultsViewModel>();
        services.AddTransient<DocumentsViewModel>();
        services.AddTransient<AiAssistantViewModel>();

        services.AddTransient<QrScanPage>();
        services.AddTransient<FaceVerifyPage>();
        services.AddTransient<HomePage>();
        services.AddTransient<ProfilePage>();
        services.AddTransient<HistoryPage>();
        services.AddTransient<ResultsPage>();
        services.AddTransient<DocumentsPage>();
        services.AddTransient<AiAssistantPage>();

        return services;
    }

    /// <summary>
    /// Acceso del paciente: se apila sobre la entrada compartida (QR → rostro), de modo que "Atrás" vuelve a elegir el
    /// tipo de acceso. Tras la verificación facial se navega al área del paciente declarada en el AppShell.
    /// </summary>
    public static void RegisterPatientRoutes()
    {
        Routing.RegisterRoute(PatientRoutes.QrAccess, typeof(QrScanPage));
        Routing.RegisterRoute(PatientRoutes.FaceVerify, typeof(FaceVerifyPage));
    }
}
