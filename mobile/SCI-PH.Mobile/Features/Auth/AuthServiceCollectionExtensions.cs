using SCI_PH.Mobile.Features.Auth.Pages;
using SCI_PH.Mobile.Features.Auth.Services;
using SCI_PH.Mobile.Features.Auth.ViewModels;

namespace SCI_PH.Mobile.Features.Auth;

public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Pantallas del acceso del personal clínico. La sesión, el almacenamiento seguro y el cliente del Mobile BFF son
    /// comunes (AddCoreServices).
    /// </summary>
    public static IServiceCollection AddAuthFeature(this IServiceCollection services)
    {
        services.AddSingleton<PendingMfaChallenge>();

        services.AddTransient<LoginViewModel>();
        services.AddTransient<MfaViewModel>();
        services.AddTransient<ClinicalAccountViewModel>();

        services.AddTransient<LoginPage>();
        services.AddTransient<MfaPage>();

        return services;
    }

    /// <summary>Login y MFA se apilan sobre la entrada; al autenticar se navega a <see cref="AuthRoutes.ClinicalArea"/>.</summary>
    public static void RegisterAuthRoutes()
    {
        Routing.RegisterRoute(AuthRoutes.Login, typeof(LoginPage));
        Routing.RegisterRoute(AuthRoutes.Mfa, typeof(MfaPage));
    }
}
