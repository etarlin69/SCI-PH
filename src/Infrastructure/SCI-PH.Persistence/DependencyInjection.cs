using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SCI_PH.Persistence.Contexts;
using SCI_PH.Persistence.Identity.Models;

namespace SCI_PH.Persistence
{
    public static class DependencyInjection
    {
        public const string ConnectionStringName = "SciPhDatabase";

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

            services.AddIdentityCore<ApplicationUser>()
                .AddRoles<IdentityRole<Guid>>()
                .AddEntityFrameworkStores<IdentityDbContext>();

            return services;
        }
    }
}
