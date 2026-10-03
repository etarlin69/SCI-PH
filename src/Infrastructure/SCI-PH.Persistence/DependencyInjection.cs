using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SCI_PH.Application.Abstractions.Security;
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
            services.AddScoped<IdentitySeeder>();
            services.AddOptions<IdentityBootstrapOptions>().Bind(configuration.GetSection(IdentityBootstrapOptions.SectionName));

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
    }
}
