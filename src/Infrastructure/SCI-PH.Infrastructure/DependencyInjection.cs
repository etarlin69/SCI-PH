using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SCI_PH.Application.Abstractions.AI;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.Features.Clinical;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using SCI_PH.Infrastructure.Biometrics.Face;
using SCI_PH.Infrastructure.Biometrics.Face.OpenCv;
using SCI_PH.Infrastructure.ClinicalAi;
using SCI_PH.Infrastructure.Email;
using SCI_PH.Infrastructure.Security;
using SCI_PH.Infrastructure.Web;

namespace SCI_PH.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            // Punto central para registrar las implementaciones de las abstracciones
            // definidas en Application (Application/Abstractions):
            // - Azure Blob Storage (IStorageService)
            // - Verificación facial (IFaceVerificationService): AddFaceVerification, solo en el Web BFF
            // - OCR (IOcrService)
            // - QR (IQrService)
            // - IA clínica asistida (IClinicalAiService): AddClinicalAi, solo en el Web BFF
            // - Otros servicios externos

            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();

            return services;
        }

        // Autenticación JWT de cuentas humanas para Web y Mobile BFF. El Device API no la usa:
        // los dispositivos tienen su propio esquema (AddDeviceAuthentication, ver ADR-007).
        // Verificación facial 1:1 asistida (ADR-012): normalización de capturas (independiente del proveedor) y el proveedor
        // elegido por configuración del servidor (FaceVerification:Provider): Fake fuera de Production, Disabled (503) en
        // Production si no se configura otro; Fake configurado en Production impide arrancar. Con "OpenCv" (proveedor local
        // real, ADR-012 PR 2) también registra su validación de arranque (modelos con hash fijado, claves, almacén), su almacén
        // de plantillas cifradas y el worker de limpieza durable; sin "OpenCv" nada de eso se registra ni necesita modelos o
        // claves.
        public static IServiceCollection AddFaceVerification(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<FaceVerificationOptions>()
                .Bind(configuration.GetSection(FaceVerificationOptions.SectionName))
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<FaceVerificationOptions>, FaceVerificationOptionsValidator>();
            services.AddSingleton<IFaceCaptureNormalizer, SkiaFaceCaptureNormalizer>();
            services.AddSingleton<FakeFaceVerificationService>();
            services.AddSingleton<IFaceVerificationService>(provider =>
                provider.GetRequiredService<IOptions<FaceVerificationOptions>>().Value.Resolve(provider.GetRequiredService<IHostEnvironment>()) switch
                {
                    FaceVerificationOptions.FakeProvider => provider.GetRequiredService<FakeFaceVerificationService>(),
                    FaceVerificationOptions.DisabledProvider => new DisabledFaceVerificationService(),
                    FaceVerificationOptions.OpenCvProvider => provider.GetRequiredService<LocalOpenCvFaceVerificationService>(),
                    var other => throw new InvalidOperationException($"FaceVerification:Provider '{other}' is not supported.")
                });

            var configured = configuration[$"{FaceVerificationOptions.SectionName}:{nameof(FaceVerificationOptions.Provider)}"];
            if (string.Equals(configured, FaceVerificationOptions.OpenCvProvider, StringComparison.Ordinal))
            {
                services.AddOpenCvFaceProvider(configuration);
            }

            return services;
        }

        // Assisted clinical AI (ADR-013, ADR-014): the provider chosen by server configuration (ClinicalAi:Provider). Fake
        // (deterministic, in process) outside Production, Disabled (503) in Production if nothing else is configured; Fake
        // configured in Production prevents the host from starting. "AzureOpenAI" sends the minimized input outside SCI-PH:
        // the host starts with it only when ClinicalAi:AllowExternalClinicalData is also true (validated on start, and
        // checked again when the provider is built) and, in Production, only with a managed identity (never an API key).
        // Nothing contacts the provider at startup. It also registers the summary cache Application consumes as a keyed
        // IMemoryCache (EncounterAiSummaryService.CacheServiceKey): a dedicated, bounded MemoryCache (SizeLimit 500 entries),
        // not the host's shared one, on the host's TimeProvider.
        public static IServiceCollection AddClinicalAi(this IServiceCollection services, IConfiguration configuration)
        {
            services.TryAddSingleton(TimeProvider.System);
            services.AddKeyedSingleton<IMemoryCache>(EncounterAiSummaryService.CacheServiceKey, (provider, _) => new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = ClinicalAiOptions.SummaryCacheSizeLimit,
                Clock = new TimeProviderClock(provider.GetRequiredService<TimeProvider>())
            }));
            services.AddOptions<ClinicalAiOptions>()
                .Bind(configuration.GetSection(ClinicalAiOptions.SectionName))
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<ClinicalAiOptions>, ClinicalAiOptionsValidator>();
            services.AddSingleton<FakeClinicalAiService>();
            services.AddSingleton<AzureOpenAiClinicalAiService>();
            services.AddSingleton<IClinicalAiService>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<ClinicalAiOptions>>().Value;
                IClinicalAiService ai = options.Resolve(provider.GetRequiredService<IHostEnvironment>()) switch
                {
                    ClinicalAiOptions.FakeProvider => provider.GetRequiredService<FakeClinicalAiService>(),
                    ClinicalAiOptions.DisabledProvider => new DisabledClinicalAiService(),
                    ClinicalAiOptions.AzureOpenAiProvider => provider.GetRequiredService<AzureOpenAiClinicalAiService>(),
                    var other => throw new InvalidOperationException($"ClinicalAi:Provider '{other}' is not supported.")
                };

                return ai.SendsDataExternally && !options.AllowExternalClinicalData
                    ? throw new InvalidOperationException("The clinical AI provider sends data outside SCI-PH and ClinicalAi:AllowExternalClinicalData is not true.")
                    : ai;
            });

            return services;
        }

        private static void AddOpenCvFaceProvider(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<OpenCvFaceOptions>()
                .Bind(configuration.GetSection(OpenCvFaceOptions.SectionName))
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<OpenCvFaceOptions>, OpenCvFaceOptionsValidator>();
            services.AddSingleton(provider => provider.GetRequiredService<IOptions<OpenCvFaceOptions>>().Value);
            services.AddSingleton<IFaceTemplateStore>(provider =>
            {
                var templates = provider.GetRequiredService<OpenCvFaceOptions>().Templates;
                return new BlobFaceTemplateStore(new BlobContainerClient(templates.ConnectionString, templates.Container));
            });
            // Disposed by the container at shutdown (every detector/recognizer pair).
            services.AddSingleton(provider =>
            {
                var options = provider.GetRequiredService<OpenCvFaceOptions>();
                var environment = provider.GetRequiredService<IHostEnvironment>();
                var yuNet = OpenCvFaceSetup.Resolve(environment, options.YuNetModelPath);
                var sFace = OpenCvFaceSetup.Resolve(environment, options.SFaceModelPath);
                return new FaceEnginePool(() => new FaceEngine(yuNet, sFace, options), options.ResolveEngineCapacity());
            });
            services.AddSingleton(provider =>
            {
                var encryption = provider.GetRequiredService<OpenCvFaceOptions>().Encryption;
                return new FaceTemplateCipher(encryption.ActiveKeyId!, OpenCvFaceSetup.LoadKeys(encryption));
            });
            services.AddSingleton(provider => new LocalOpenCvFaceVerificationService(
                provider.GetRequiredService<FaceEnginePool>(),
                provider.GetRequiredService<IFaceTemplateStore>(),
                provider.GetRequiredService<FaceTemplateCipher>(),
                provider.GetRequiredService<OpenCvFaceOptions>(),
                provider.GetRequiredService<ILogger<LocalOpenCvFaceVerificationService>>()));
            services.AddSingleton<FaceTemplateReconciler>();
            // Started in registration order: the provider (native runtime + models) is built at startup, before any
            // request, and a failure stops the host; then the cleanup worker.
            services.AddHostedService<OpenCvFaceProviderStartup>();
            services.AddHostedService<FaceTemplateCleanupWorker>();
        }

        public static IServiceCollection AddJwtSecurity(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName)).ValidateOnStart();
            services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
            services.AddSingleton<ITokenService, JwtTokenService>();

            services.AddHttpContextAccessor();
            services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
            services.AddScoped<IAuthorizationHandler, PatientRecordAuthorizationHandler>();

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
                {
                    // Keep short JWT claim names (sub, role, permission) instead of mapping them to long URIs.
                    bearer.MapInboundClaims = false;
                    bearer.TokenValidationParameters = JwtTokenService.CreateValidationParameters(jwt.Value);
                    bearer.Events = new JwtBearerEvents { OnTokenValidated = RejectNonAccessTokens };
                });

            var authorization = services.AddAuthorizationBuilder()
                .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
            foreach (var permission in Permissions.All)
            {
                authorization.AddPolicy(permission, policy =>
                    policy.RequireAuthenticatedUser().RequireClaim(CustomClaimTypes.Permission, permission));
            }

            return services;
        }

        // Email for invitations, confirmation and password reset (Web BFF).
        public static IServiceCollection AddEmailDelivery(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<SmtpOptions>().Bind(configuration.GetSection(SmtpOptions.SectionName));
            services.AddOptions<EmailDeliveryOptions>().Bind(configuration.GetSection(EmailDeliveryOptions.SectionName)).ValidateOnStart();
            services.AddSingleton<IValidateOptions<EmailDeliveryOptions>, EmailDeliveryOptionsValidator>();
            services.AddScoped<IEmailSender>(provider =>
                provider.GetRequiredService<IOptions<EmailDeliveryOptions>>().Value.Mode switch
                {
                    EmailDeliveryMode.Smtp => ActivatorUtilities.CreateInstance<SmtpEmailSender>(provider),
                    EmailDeliveryMode.PickupDirectory => ActivatorUtilities.CreateInstance<PickupDirectoryEmailSender>(provider),
                    _ => ActivatorUtilities.CreateInstance<DisabledEmailSender>(provider)
                });

            return services;
        }

        // Explicit browser origins for the Web BFF; required because the refresh cookie uses credentials.
        public static IServiceCollection AddFrontendCors(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<FrontendSecurityOptions>().Bind(configuration.GetSection(FrontendSecurityOptions.SectionName)).ValidateOnStart();
            services.AddSingleton<IValidateOptions<FrontendSecurityOptions>, FrontendSecurityOptionsValidator>();
            services.AddCors();
            services.AddOptions<CorsOptions>().Configure<IOptions<FrontendSecurityOptions>>((cors, frontend) =>
                cors.AddPolicy(FrontendCors.PolicyName, policy => policy
                    .WithOrigins(frontend.Value.AllowedOrigins)
                    .AllowCredentials()
                    .WithHeaders("Authorization", "Content-Type", FrontendCors.CsrfHeaderName)
                    .WithMethods("GET", "POST", "PUT", "DELETE")
                    .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));

            return services;
        }

        // Device API only: device code + key, independent from ASP.NET Core Identity and JWT.
        public static IServiceCollection AddDeviceAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAuthentication(DeviceAuthenticationDefaults.Scheme)
                .AddScheme<DeviceApiKeyAuthenticationOptions, DeviceApiKeyAuthenticationHandler>(
                    DeviceAuthenticationDefaults.Scheme,
                    options => configuration.GetSection(DeviceAuthenticationDefaults.SectionName).Bind(options));

            var authorization = services.AddAuthorizationBuilder()
                .SetFallbackPolicy(new AuthorizationPolicyBuilder(DeviceAuthenticationDefaults.Scheme).RequireAuthenticatedUser().Build());
            foreach (var scope in DeviceScopes.All)
            {
                authorization.AddPolicy(scope, policy => policy
                    .AddAuthenticationSchemes(DeviceAuthenticationDefaults.Scheme)
                    .RequireAuthenticatedUser()
                    .RequireClaim(CustomClaimTypes.DeviceScope, scope));
            }

            return services;
        }

        // A signed token is accepted on normal endpoints only if it is an access token (not an MFA challenge).
        private static Task RejectNonAccessTokens(TokenValidatedContext context)
        {
            if (context.Principal?.FindFirst(CustomClaimTypes.TokenUse)?.Value != TokenUses.Access)
            {
                context.Fail("Token is not an access token.");
            }

            return Task.CompletedTask;
        }
    }
}
