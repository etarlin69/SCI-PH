using SCI_PH.Mobile.Features.Patient.Ai;
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
    /// Registra el módulo del paciente. Su sesión es la de la cuenta (inicio de sesión común, ADR-015): no hay sesión
    /// propia del paciente ni acceso por QR o rostro.
    /// </summary>
    public static IServiceCollection AddPatientFeature(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPatientDataService, MockPatientDataService>();
        services.AddSingleton<IPatientAssistantService, MockPatientAssistantService>();

        // Navegación y diálogos son los comunes de la app (AddCoreServices).
        // Perfil: demostración local de foto y descripción.
        services.AddSingleton(Preferences.Default);
        services.AddSingleton(MediaPicker.Default);
        services.AddSingleton(FileSystem.Current);

        services.AddTransient<HomeViewModel>();
        services.AddTransient<ProfileViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<ResultsViewModel>();
        services.AddTransient<DocumentsViewModel>();
        services.AddTransient<AiAssistantViewModel>();

        services.AddTransient<HomePage>();
        services.AddTransient<ProfilePage>();
        services.AddTransient<HistoryPage>();
        services.AddTransient<ResultsPage>();
        services.AddTransient<DocumentsPage>();
        services.AddTransient<AiAssistantPage>();

        return services;
    }
}
