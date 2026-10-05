using SCI_PH.Mobile.App.Services;

namespace SCI_PH.Mobile.App.Configuration;

public static class DependencyInjection
{
    // 1. Registro de integraciones externas y servicios de negocio
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, string apiBaseUrl)
    {
        services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
        services.AddScoped<IAuthService, ApiAuthService>();
        
        return services;
    }

    // 2. Registro de la capa de presentación (ViewModels)
    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        // services.AddTransient<LoginViewModel>();
        // services.AddTransient<HomeViewModel>();
        
        return services;
    }

    // 3. Registro de las pantallas (Views/Pages)
    public static IServiceCollection AddViews(this IServiceCollection services)
    {
        // services.AddTransient<LoginPage>();
        // services.AddTransient<HomePage>();
        
        return services;
    }
}