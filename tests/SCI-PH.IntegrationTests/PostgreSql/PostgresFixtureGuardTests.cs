namespace SCI_PH.IntegrationTests.PostgreSql
{
    // Issue #45: without Docker (fixture not started) a PostgreSQL test must skip, and one that forgot its
    // Skip.IfNot(Available) must fail with a clear message, never a NullReferenceException. With SCIPH_REQUIRE_DOCKER=true
    // the fixture's InitializeAsync already fails the run instead of skipping.
    public sealed class PostgresFixtureGuardTests
    {
        [Fact]
        public async Task AFixtureWithoutPostgresIsUnavailableAndSaysWhy()
        {
            var fixture = new PostgresFixture(); // InitializeAsync never ran: as when Docker is not available

            Assert.False(fixture.Available);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateDatabaseAsync());
            Assert.Contains("PostgreSQL is not available", error.Message);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateMigratedDatabaseAsync());
        }
    }
}
