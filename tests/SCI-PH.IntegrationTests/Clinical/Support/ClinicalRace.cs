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

        // Another writer receives the transfer at the destination (as TransferService.ReceiveAsync) in a transaction that
        // the caller commits.
        public static async Task<(NpgsqlConnection Connection, NpgsqlTransaction Receive)> BeginReceiveAsync(
            string connectionString, Guid transferId, Guid encounterId, Guid clinicId, Guid staffId)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync();
            await using (var transfer = new NpgsqlCommand(
                """UPDATE clinical."ClinicalTransfers" SET "Status" = 3, "ReceivedAtUtc" = GREATEST(now(), "SentAtUtc"), "ReceivedByStaffId" = @staff WHERE "Id" = @id AND "Status" = 2""",
                connection, transaction))
            {
                transfer.Parameters.AddWithValue("id", transferId);
                transfer.Parameters.AddWithValue("staff", staffId);
                Assert.Equal(1, await transfer.ExecuteNonQueryAsync());
            }

            await using (var encounter = new NpgsqlCommand(
                """UPDATE clinical."ClinicalEncounters" SET "Status" = 4, "ClinicId" = @clinic WHERE "Id" = @id AND "Status" = 3""",
                connection, transaction))
            {
                encounter.Parameters.AddWithValue("id", encounterId);
                encounter.Parameters.AddWithValue("clinic", clinicId);
                Assert.Equal(1, await encounter.ExecuteNonQueryAsync());
            }

            return (connection, transaction);
        }

        // Another writer identifies the NN encounter (as EncounterService.IdentifyPatientAsync) in a transaction that the caller
        // commits.
        public static async Task<(NpgsqlConnection Connection, NpgsqlTransaction Identify)> BeginIdentifyAsync(string connectionString, Guid encounterId, Guid patientId, Guid staffId)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync();
            await using var update = new NpgsqlCommand(
                """UPDATE clinical."ClinicalEncounters" SET "PatientId" = @patient, "IdentifiedByStaffId" = @staff, "IdentifiedAtUtc" = GREATEST(now(), "StartedAtUtc") WHERE "Id" = @id AND "PatientId" IS NULL""",
                connection, transaction);
            update.Parameters.AddWithValue("id", encounterId);
            update.Parameters.AddWithValue("patient", patientId);
            update.Parameters.AddWithValue("staff", staffId);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
            return (connection, transaction);
        }

        // Another writer revokes the biometric reference (as PatientBiometricReference.Revoke) in a transaction that the
        // caller commits.
        public static async Task<(NpgsqlConnection Connection, NpgsqlTransaction Revoke)> BeginRevokeReferenceAsync(string connectionString, Guid referenceId)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync();
            await using var update = new NpgsqlCommand(
                """UPDATE clinical."PatientBiometricReferences" SET "Status" = 2, "RevokedAtUtc" = GREATEST(now(), "CreatedAtUtc") WHERE "Id" = @id AND "Status" = 1""",
                connection, transaction);
            update.Parameters.AddWithValue("id", referenceId);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
            return (connection, transaction);
        }

        // Holds the row lock an UPDATE of the reference would take (FOR NO KEY UPDATE), without changing it, until the caller
        // ends the transaction. Writers of the reference then queue on the row in arrival order (an insert that only references
        // it, such as a deletion job, does not wait: FOR KEY SHARE does not conflict with this lock).
        public static async Task<(NpgsqlConnection Connection, NpgsqlTransaction Hold)> HoldReferenceAsync(string connectionString, Guid referenceId)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync();
            await using var select = new NpgsqlCommand(
                """SELECT 1 FROM clinical."PatientBiometricReferences" WHERE "Id" = @id FOR NO KEY UPDATE""", connection, transaction);
            select.Parameters.AddWithValue("id", referenceId);
            Assert.Equal(1, await select.ExecuteScalarAsync());
            return (connection, transaction);
        }

        // Holds the row lock an UPDATE of the encounter would take, without changing it, until the caller ends the
        // transaction: a request that writes the encounter (an identification) waits there, after all its checks.
        public static async Task<(NpgsqlConnection Connection, NpgsqlTransaction Hold)> HoldEncounterAsync(string connectionString, Guid encounterId)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var transaction = await connection.BeginTransactionAsync();
            await using var select = new NpgsqlCommand(
                """SELECT 1 FROM clinical."ClinicalEncounters" WHERE "Id" = @id FOR NO KEY UPDATE""", connection, transaction);
            select.Parameters.AddWithValue("id", encounterId);
            Assert.Equal(1, await select.ExecuteScalarAsync());
            return (connection, transaction);
        }

        // Waits until at least `waiters` sessions of the database are waiting on a lock, while none of the requests finished.
        // When several wait on the same row, PostgreSQL does not promise which one goes first once it is released.
        public static async Task WaitUntilWaitingOnLocksAsync(string connectionString, int waiters, params Task<HttpResponseMessage>[] requests)
        {
            await using var monitor = new NpgsqlConnection(connectionString);
            await monitor.OpenAsync();
            var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
            for (var attempt = 0; attempt < 200 && !requests.Any(r => r.IsCompleted); attempt++)
            {
                await using var query = new NpgsqlCommand(
                    "SELECT count(*) FROM pg_stat_activity WHERE datname = @db AND wait_event_type = 'Lock'", monitor);
                query.Parameters.AddWithValue("db", database!);
                if ((long)(await query.ExecuteScalarAsync())! >= waiters)
                {
                    return;
                }

                await Task.Delay(50);
            }

            var finished = requests.Where(r => r.IsCompleted).Select(r => r.Result.StatusCode.ToString()).ToList();
            Assert.Fail(finished.Count > 0
                ? $"A request finished before the lock was released ({string.Join(", ", finished)})."
                : $"Fewer than {waiters} requests waited on the row.");
        }

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
