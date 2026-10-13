using SCI_PH.PatientPrototype.Features.Patient.Ai;
using SCI_PH.PatientPrototype.Features.Patient.Auth;
using SCI_PH.PatientPrototype.Features.Patient.Documents;
using SCI_PH.PatientPrototype.Features.Patient.History;
using SCI_PH.PatientPrototype.Features.Patient.Home;
using SCI_PH.PatientPrototype.Features.Patient.Profile;
using SCI_PH.PatientPrototype.Features.Patient.Results;
using SCI_PH.PatientPrototype.Features.Patient.Services;
using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;
using SCI_PH.PatientPrototype.Features.Patient.Services.Mocks;
using SCI_PH.PatientPrototype.Features.Patient.ViewModels;

namespace SCI_PH.PatientPrototype.Features.Patient;

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

        services.AddSingleton<IPatientNavigator, ShellPatientNavigator>();
        services.AddSingleton<IPatientDialogService, ShellPatientDialogService>();
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

    /// <summary>Rutas que no están en la jerarquía del Shell (se apilan sobre la página actual).</summary>
    public static void RegisterPatientRoutes()
    {
        Routing.RegisterRoute(PatientRoutes.FaceVerify, typeof(FaceVerifyPage));
    }
}
