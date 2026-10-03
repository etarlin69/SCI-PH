using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Persistence.Contexts;

namespace SCI_PH.IntegrationTests.Security.Support
{
    internal static class TestStores
    {
        // Replaces the Npgsql registrations made by AddPersistence. In-memory: no connection is ever opened.
        public static IServiceCollection UseInMemoryStores(this IServiceCollection services, string databaseName) =>
            services.UseStores(
                options => options.UseInMemoryDatabase($"{databaseName}-identity")
                    .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)),
                options => options.UseInMemoryDatabase($"{databaseName}-clinical"));

        // Real PostgreSQL (Testcontainers) with each context keeping its own migration history.
        public static IServiceCollection UsePostgresStores(this IServiceCollection services, string connectionString) =>
            services.UseStores(
                options => options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema)),
                options => options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ClinicalDbContext.Schema)));

        public static IServiceCollection UseFakeEmail(this IServiceCollection services, FakeEmailSender sender)
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(sender);
            return services;
        }

        private static IServiceCollection UseStores(
            this IServiceCollection services,
            Action<DbContextOptionsBuilder> identity,
            Action<DbContextOptionsBuilder> clinical)
        {
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
            services.RemoveAll<DbContextOptions<ClinicalDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ClinicalDbContext>>();
            services.AddDbContext<IdentityDbContext>(identity);
            services.AddDbContext<ClinicalDbContext>(clinical);
            return services;
        }
    }
}
