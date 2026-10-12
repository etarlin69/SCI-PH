using Npgsql;

namespace SCI_PH.IntegrationTests.Clinical.Support
{
    // Deterministic race between closing an encounter and a clinical write: another transaction closes the encounter and
    // stays open while the request runs, so the request reads the committed state (still open), passes every check and
    // then blocks on the encounter row until the close commits.
    internal static class ClinicalRace
    {
        // Closes an InProgress encounter in a transaction that the caller commits.
        public static Task<(NpgsqlConnection Connection, NpgsqlTransaction Close)> BeginCloseAsync(string connectionString, Guid encounterId) =>
            BeginAsync(connectionString, encounterId,
                """UPDATE clinical."ClinicalEncounters" SET "Status" = 6, "EndedAtUtc" = GREATEST(now(), "StartedAtUtc") WHERE "Id" = @id AND "Status" = 2""");

        // Another writer moves the encounter from one status to another (e.g. a cancelled transfer: Transferred -> InProgress)
        // in a transaction that the caller commits.
        public static Task<(NpgsqlConnection Connection, NpgsqlTransaction Change)> BeginStatusChangeAsync(
            string connectionString, Guid encounterId, int from, int to) =>
            BeginAsync(connectionString, encounterId,
                $"""UPDATE clinical."ClinicalEncounters" SET "Status" = {to} WHERE "Id" = @id AND "Status" = {from}""");

        private static async Task<(NpgsqlConnection, NpgsqlTransaction)> BeginAsync(string connectionString, Guid encounterId, string sql)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync();
            await using var update = new NpgsqlCommand(sql, connection, transaction);
            update.Parameters.AddWithValue("id", encounterId);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
            return (connection, transaction);
        }

        public static async Task WaitUntilBlockedOnLockAsync(string connectionString, Task<HttpResponseMessage> request)
        {
            await using var monitor = new NpgsqlConnection(connectionString);
            await monitor.OpenAsync();
            var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
            for (var attempt = 0; attempt < 200 && !request.IsCompleted; attempt++)
            {
                await using var query = new NpgsqlCommand(
                    "SELECT count(*) FROM pg_stat_activity WHERE datname = @db AND wait_event_type = 'Lock'", monitor);
                query.Parameters.AddWithValue("db", database!);
                if ((long)(await query.ExecuteScalarAsync())! > 0)
                {
                    return;
                }

                await Task.Delay(50);
            }

            var finishedEarly = request.IsCompleted ? (await request).StatusCode.ToString() : null;
            Assert.Fail(finishedEarly is not null
                ? $"The clinical write finished before the close committed ({finishedEarly})."
                : "The clinical write never waited on the encounter row.");
        }
    }
}
