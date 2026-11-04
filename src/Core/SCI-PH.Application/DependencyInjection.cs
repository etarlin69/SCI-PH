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

        // Casos de uso clínicos (pacientes, atenciones, admisión, registros clínicos, medicación, traslados y emergencia
        // prehospitalaria, con su identificación por huella, y el enrolamiento de huellas). Requiere AddAccountSecurity (IPatientAccessAuthorizer), AddJwtSecurity (ICurrentUser) y
        // AddPersistence (stores clínicos y recibos de idempotencia).
        public static IServiceCollection AddClinicalWorkflows(this IServiceCollection services)
        {
            services.AddScoped<ICurrentClinicalActor, CurrentClinicalActor>();
            services.AddScoped<PatientService>();
            services.AddScoped<EncounterService>();
            services.AddScoped<AdmissionService>();
            services.AddScoped<EncounterRecordService>();
            services.AddScoped<MedicationService>();
            services.AddScoped<TransferService>();
            services.AddScoped<ClinicalOrganizationService>();
            services.AddScoped<IEncounterAccessService, EncounterAccessService>();
            services.AddScoped<PrehospitalEmergencyService>();
            services.AddScoped<EmergencyFingerprintService>();
            services.AddScoped<FingerprintEnrollmentService>();
            services.AddScoped<FingerprintRevocationService>();

            return services;
        }

        // Autoservicio del paciente (ADR-015, solo Mobile BFF): lectura de su propio expediente, resuelto desde la cuenta.
        // Requiere AddAccountSecurity (IPatientAccessAuthorizer), AddJwtSecurity (ICurrentUser) y AddPersistence.
        public static IServiceCollection AddPatientSelfService(this IServiceCollection services)
        {
            services.AddScoped<PatientSelfService>();

            return services;
        }

        // Assisted 1:1 face verification (ADR-012, Web BFF only). Requires a face provider and a capture normalizer
        // (Infrastructure: AddFaceVerification).
        public static IServiceCollection AddFaceVerificationWorkflow(this IServiceCollection services)
        {
            services.AddScoped<PatientFaceService>();
            services.AddScoped<FaceTemplateCleanupService>();

            return services;
        }

        // Assisted clinical AI (ADR-013, ADR-014, Web BFF only): closed, read-only operations and the "SCI-PH.ClinicalAi"
        // meter (one per host). Requires a clinical AI provider and the summary cache (Infrastructure: AddClinicalAi) and
        // AddClinicalWorkflows.
        public static IServiceCollection AddClinicalAiWorkflow(this IServiceCollection services)
        {
            services.AddScoped<EncounterAiSummaryService>();
            services.AddSingleton<ClinicalAiMetrics>();

            return services;
        }

        // Autenticación de dispositivos y lado del lector de huella (solo Device API). Independiente de las cuentas humanas.
        // Requiere AddPersistence (credenciales de dispositivo, sesiones de huella y stores clínicos).
        public static IServiceCollection AddDeviceSecurity(this IServiceCollection services)
        {
            services.AddScoped<DeviceAuthenticationService>();
            services.AddScoped<FingerprintReaderService>();
            services.AddScoped<FingerprintEnrollmentReaderService>();
            services.AddScoped<FingerprintSlotDeletionReaderService>();
            services.AddScoped<FingerprintSlotInventoryReaderService>();

            return services;
        }
    }
}
