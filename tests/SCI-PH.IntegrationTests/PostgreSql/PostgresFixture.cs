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
                // Every test gets its own database and hosts, and the concurrency tests open several connections at once, so
                // the shared test server keeps headroom above the default 100 connections. Each database's pool is released
                // when its owner is disposed (ReleaseDatabase), so finished tests do not keep connections open.
                _container = new PostgreSqlBuilder(Image).WithCommand("-c", "max_connections=400").Build();
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

        // Npgsql keeps one process-wide pool per connection string (no NpgsqlDataSource is involved), and disposing a host
        // or a DbContext does not close it: its idle connections stay open until Npgsql prunes them (300 s by default).
        // Every test has its own database, so its own pool; the owner of a test database (a host, a security context, a
        // test) releases it once nothing uses it any more. Only that exact pool is cleared, never another test's.
        public static void ReleaseDatabase(string connectionString)
        {
            using var connection = new NpgsqlConnection(connectionString);
            NpgsqlConnection.ClearPool(connection);
        }

        // Server-side sessions (pg_stat_activity) currently connected to the database of the connection string, other than
        // the one asking.
        public async Task<long> CountConnectionsAsync(string connectionString)
        {
            var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
            await using var connection = new NpgsqlConnection(_container!.GetConnectionString());
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "select count(*) from pg_stat_activity where datname = @database and pid <> pg_backend_pid()", connection);
            command.Parameters.AddWithValue("database", database!);
            return (long)(await command.ExecuteScalarAsync())!;
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
