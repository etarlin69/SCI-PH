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
        services.AddSingleton<IAccountSecurityService, AccountSecurityService>();
        services.AddTransient<AccountSecurityViewModel>();

        services.AddTransient<LoginPage>();
        services.AddTransient<MfaPage>();
        services.AddTransient<AccountSecurityPage>();

        return services;
    }

    /// <summary>
    /// Login y MFA se apilan sobre la entrada; al autenticar se navega a <see cref="AuthRoutes.ClinicalArea"/>. La seguridad
    /// de la cuenta se apila sobre el área (paciente o paramédico).
    /// </summary>
    public static void RegisterAuthRoutes()
    {
        Routing.RegisterRoute(AuthRoutes.Login, typeof(LoginPage));
        Routing.RegisterRoute(AuthRoutes.Mfa, typeof(MfaPage));
        Routing.RegisterRoute(AuthRoutes.AccountSecurity, typeof(AccountSecurityPage));
    }
}
