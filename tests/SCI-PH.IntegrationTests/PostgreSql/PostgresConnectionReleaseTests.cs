using SCI_PH.Application.Common.Security;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.PostgreSql
{
    // Every test owns a database, and Npgsql keeps one process-wide pool per connection string that disposing a host does
    // not close. Without releasing it, the idle connections of every finished test stay open until Npgsql prunes them
    // (300 s by default), and the suite runs the shared server out of connections (issue #52: 53300 too many clients).
    // After its owner is disposed, a test database must have no session left.
    [Collection(PostgresCollection.Name)]
    public sealed class PostgresConnectionReleaseTests
    {
        private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(10);
        private readonly PostgresFixture _postgres;

        public PostgresConnectionReleaseTests(PostgresFixture postgres) => _postgres = postgres;

        [SkippableFact]
        public async Task AWebHostLeavesNoConnectionToItsDatabase()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var host = await ClinicalApiHost.StartAsync(_postgres);
            string connectionString;
            await using (host)
            {
                connectionString = host.ConnectionString;
                var clinic = await host.CreateClinicAsync();
                await host.SignInAsync(SecurityRoles.Receptionist, [clinic]);
                // Concurrent work, so the pool holds several connections at once.
                await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => host.SeedPatientAsync()));
                Assert.True(await _postgres.CountConnectionsAsync(connectionString) > 0, "The pool keeps the idle connections while the host runs.");
            }

            await AssertReleasedAsync(connectionString);
        }

        [SkippableFact]
        public async Task TheMobileAndDeviceHostsLeaveNoConnectionToTheirDatabase()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var host = await MobileEmergencyHost.StartAsync(_postgres);
            string connectionString;
            await using (host)
            {
                connectionString = host.ConnectionString;
                var clinic = await host.Web.CreateClinicAsync();
                await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
                await host.ProvisionReaderAsync();
                Assert.True(await _postgres.CountConnectionsAsync(connectionString) > 0, "The pool keeps the idle connections while the hosts run.");
            }

            await AssertReleasedAsync(connectionString);
        }

        [SkippableFact]
        public async Task ASecurityContextLeavesNoConnectionToItsDatabase()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var connectionString = await _postgres.CreateMigratedDatabaseAsync();
            await using (var context = new SecurityTestContext(postgresConnectionString: connectionString))
            {
                var user = await context.CreateUserAsync(SecurityRoles.Doctor);
                Assert.NotNull(await context.LoginAsync(user.Email, user.Password));
                Assert.True(await _postgres.CountConnectionsAsync(connectionString) > 0, "The pool keeps the idle connections while the context runs.");
            }

            await AssertReleasedAsync(connectionString);
        }

        // A closed connection leaves pg_stat_activity once its backend has exited, which takes a moment after the client
        // closes it: bounded polling instead of assuming it is instantaneous.
        private async Task AssertReleasedAsync(string connectionString)
        {
            var deadline = DateTime.UtcNow + ReleaseTimeout;
            long remaining;
            while ((remaining = await _postgres.CountConnectionsAsync(connectionString)) > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            Assert.True(remaining == 0, $"{remaining} connection(s) to the test database are still open after its owner was disposed.");
        }
    }
}
