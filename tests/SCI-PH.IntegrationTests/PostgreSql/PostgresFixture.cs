using Microsoft.EntityFrameworkCore;
using Npgsql;
using SCI_PH.Persistence.Contexts;
using Testcontainers.PostgreSql;

namespace SCI_PH.IntegrationTests.PostgreSql
{
    // One disposable PostgreSQL 17 container per test run; every test gets its own database.
    // Without Docker the tests are skipped locally; CI sets SCIPH_REQUIRE_DOCKER=true so they must run.
    public sealed class PostgresFixture : IAsyncLifetime
    {
        public const string Image = "postgres:17";
        private PostgreSqlContainer? _container;

        public bool Available { get; private set; }

        public string SkipReason { get; private set; } = "Docker is not available.";

        private static bool DockerRequired =>
            string.Equals(Environment.GetEnvironmentVariable("SCIPH_REQUIRE_DOCKER"), "true", StringComparison.OrdinalIgnoreCase);

        public async Task InitializeAsync()
        {
            try
            {
                _container = new PostgreSqlBuilder(Image).Build();
                await _container.StartAsync();
                Available = true;
            }
            catch (Exception exception) when (!DockerRequired)
            {
                SkipReason = $"Docker is not available ({exception.GetType().Name}); PostgreSQL tests skipped.";
            }
        }

        public async Task<string> CreateDatabaseAsync()
        {
            var name = $"sciph_test_{Guid.NewGuid():N}";
            await using (var connection = new NpgsqlConnection(_container!.GetConnectionString()))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
                await command.ExecuteNonQueryAsync();
            }

            return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
        }

        public static IdentityDbContext Identity(string connectionString) => new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema))
            .Options);

        public static ClinicalDbContext Clinical(string connectionString) => new(new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ClinicalDbContext.Schema))
            .Options);

        public async Task<string> CreateMigratedDatabaseAsync()
        {
            var connectionString = await CreateDatabaseAsync();
            await using var identity = Identity(connectionString);
            await identity.Database.MigrateAsync();
            await using var clinical = Clinical(connectionString);
            await clinical.Database.MigrateAsync();
            return connectionString;
        }

        public async Task DisposeAsync()
        {
            if (_container is not null)
            {
                await _container.DisposeAsync();
            }
        }
    }

    [CollectionDefinition(Name)]
    public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
    {
        public const string Name = "PostgreSQL";
    }
}
