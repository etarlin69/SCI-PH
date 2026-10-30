using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Persistence.Clinical.Seed;
using SCI_PH.Persistence.Clinical.Services;
using SCI_PH.Persistence.Contexts;
using SCI_PH.Persistence.Identity;
using SCI_PH.Persistence.Identity.Models;
using SCI_PH.Persistence.Identity.Seed;
using SCI_PH.Persistence.Identity.Services;

namespace SCI_PH.Persistence
{
    public static class DependencyInjection
    {
        public const string ConnectionStringName = "SciPhDatabase";
        public const string DataProtectionApplicationName = "SCI-PH";

        public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException(
                    $"No se encontró la cadena de conexión 'ConnectionStrings:{ConnectionStringName}'. " +
                    "Configúrala con user-secrets o con la variable de entorno ConnectionStrings__SciPhDatabase.");

            // Un solo PostgreSQL; cada contexto guarda su historial de migraciones en su propio schema.
            services.AddDbContext<IdentityDbContext>(options =>
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema)));

            services.AddDbContext<ClinicalDbContext>(options =>
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ClinicalDbContext.Schema)));

            services.TryAddSingleton(TimeProvider.System);

            // Email confirmation and password reset tokens are protected with this key ring. Keeping it in the
            // identity schema makes the links valid across restarts and hosts. In production the keys should
            // additionally be encrypted at rest (for example with Azure Key Vault).
            services.AddDataProtection()
                .SetApplicationName(DataProtectionApplicationName)
                .PersistKeysToDbContext<IdentityDbContext>();

            services.AddIdentityCore<ApplicationUser>(IdentityPolicy.Configure)
                .AddRoles<IdentityRole<Guid>>()
                .AddEntityFrameworkStores<IdentityDbContext>()
                .AddDefaultTokenProviders();

            services.AddScoped<IIdentityService, IdentityService>();
            services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
            services.AddScoped<ISecurityAuditLog, SecurityAuditLog>();
            services.AddScoped<IAuthenticatorService, AuthenticatorService>();
            services.AddScoped<IMfaChallengeStore, MfaChallengeStore>();
            services.AddScoped<IAccountTokenService, AccountTokenService>();
            services.AddScoped<IUserDirectory, UserDirectory>();
            services.AddScoped<IAccountInvitationStore, AccountInvitationStore>();
            services.AddScoped<IDeviceCredentialStore, DeviceCredentialStore>();
            services.AddScoped<IClinicalIdentityLinkService, ClinicalIdentityLinkService>();
            services.AddScoped<IClinicalStaffDirectory, ClinicalStaffDirectory>();
            services.AddScoped<IClinicalReadStore, ClinicalReadStore>();
            services.AddScoped<IClinicalOrganizationStore, ClinicalOrganizationStore>();
            services.AddScoped<IClinicalWriteStore, ClinicalWriteStore>();
            services.AddScoped<IIdempotencyStore, ClinicalIdempotencyStore>();
            services.AddScoped<IFingerprintSessionStore, FingerprintSessionStore>();
            services.AddScoped<IFingerprintEnrollmentStore, FingerprintEnrollmentStore>();
            services.AddScoped<IFingerprintSlotDeletionStore, FingerprintSlotDeletionStore>();
            services.AddScoped<IFaceTemplateDeletionStore, FaceTemplateDeletionStore>();
            services.AddScoped<IdentitySeeder>();
            services.AddOptions<IdentityBootstrapOptions>().Bind(configuration.GetSection(IdentityBootstrapOptions.SectionName));
            services.AddScoped<IdentityDevelopmentAccountSeeder>();
            services.AddOptions<IdentityDevelopmentAccountsOptions>().Bind(configuration.GetSection(IdentityDevelopmentAccountsOptions.SectionName));
            services.AddScoped<ClinicalDevelopmentSeeder>();
            services.AddOptions<ClinicalDevelopmentSeedOptions>().Bind(configuration.GetSection(ClinicalDevelopmentSeedOptions.SectionName));

            return services;
        }

        // Ejecuta el seeder solo si IdentityBootstrap:Enabled es true. Requiere las migraciones de Identity aplicadas.
        public static async Task RunIdentityBootstrapAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
        {
            await using var scope = services.CreateAsyncScope();
            if (!scope.ServiceProvider.GetRequiredService<IOptions<IdentityBootstrapOptions>>().Value.Enabled)
            {
                return;
            }

            await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(cancellationToken);
        }

        // Cuentas de prueba (una por rol): SOLO en Development y con IdentityDevelopmentAccounts:Enabled = true. Activada
        // fuera de Development, el host no arranca: es una configuración peligrosa, no algo que ignorar en silencio.
        // Requiere los roles de RunIdentityBootstrapAsync.
        public static async Task RunIdentityDevelopmentAccountsAsync(
            this IServiceProvider services, IHostEnvironment environment, CancellationToken cancellationToken = default)
        {
            await using var scope = services.CreateAsyncScope();
            if (!scope.ServiceProvider.GetRequiredService<IOptions<IdentityDevelopmentAccountsOptions>>().Value.Enabled)
            {
                return;
            }

            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"IdentityDevelopmentAccounts:Enabled is only allowed in Development (current: {environment.EnvironmentName}).");
            }

            await scope.ServiceProvider.GetRequiredService<IdentityDevelopmentAccountSeeder>().SeedAsync(cancellationToken);
        }

        // Datos clínicos de demostración: SOLO en Development y con ClinicalDevelopmentSeed:Enabled = true.
        // En cualquier otro entorno no hace nada aunque la opción esté activada. Requiere las migraciones aplicadas.
        public static async Task RunClinicalDevelopmentSeedAsync(
            this IServiceProvider services, IHostEnvironment environment, CancellationToken cancellationToken = default)
        {
            if (!environment.IsDevelopment())
            {
                return;
            }

            await using var scope = services.CreateAsyncScope();
            if (!scope.ServiceProvider.GetRequiredService<IOptions<ClinicalDevelopmentSeedOptions>>().Value.Enabled)
            {
                return;
            }

            await scope.ServiceProvider.GetRequiredService<ClinicalDevelopmentSeeder>().SeedAsync(cancellationToken);
        }
    }
}
