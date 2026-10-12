using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Features.Accounts;
using SCI_PH.Application.Features.Authentication;
using SCI_PH.Application.Features.Authorization;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Application.Features.Devices;

namespace SCI_PH.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            // Punto central para registrar servicios de la capa Application
            // (casos de uso, validadores, comportamientos, etc.).

            return services;
        }

        // Sesiones de cuentas humanas (Web y Mobile BFF): login, MFA, refresh, cambio de contraseña y
        // autorización por recurso. Requieren AddPersistence y AddJwtSecurity.
        public static IServiceCollection AddAccountSecurity(this IServiceCollection services)
        {
            services.AddScoped<AuthenticationService>();
            services.AddScoped<IPatientAccessAuthorizer, PatientAccessAuthorizer>();

            return services;
        }

        // Gestión de cuentas (solo Web BFF): invitaciones, recuperación, confirmación de email, MFA,
        // administración de usuarios y de dispositivos. Requiere además AddEmailDelivery.
        public static IServiceCollection AddAccountManagement(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<AccountEmailOptions>()
                .Bind(configuration.GetSection(AccountEmailOptions.SectionName))
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<AccountEmailOptions>, AccountEmailOptionsValidator>();

            services.AddScoped<UserAccountService>();
            services.AddScoped<AccountInvitationService>();
            services.AddScoped<AccountRecoveryService>();
            services.AddScoped<MfaManagementService>();
            services.AddScoped<DeviceCredentialService>();

            return services;
        }

        // Casos de uso clínicos (pacientes, atenciones, admisión, registros clínicos y medicación). Requiere AddAccountSecurity
        // (IPatientAccessAuthorizer), AddJwtSecurity (ICurrentUser) y AddPersistence (stores clínicos).
        public static IServiceCollection AddClinicalWorkflows(this IServiceCollection services)
        {
            services.AddScoped<ICurrentClinicalActor, CurrentClinicalActor>();
            services.AddScoped<PatientService>();
            services.AddScoped<EncounterService>();
            services.AddScoped<AdmissionService>();
            services.AddScoped<EncounterRecordService>();
            services.AddScoped<MedicationService>();

            return services;
        }

        // Autenticación de dispositivos (solo Device API). Independiente de las cuentas humanas.
        public static IServiceCollection AddDeviceSecurity(this IServiceCollection services)
        {
            services.AddScoped<DeviceAuthenticationService>();

            return services;
        }
    }
}
