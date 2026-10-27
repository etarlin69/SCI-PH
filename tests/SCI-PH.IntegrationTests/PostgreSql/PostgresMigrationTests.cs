using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace SCI_PH.IntegrationTests.PostgreSql
{
    [Collection(PostgresCollection.Name)]
    public sealed class PostgresMigrationTests
    {
        private const string InitialIdentity = "20260927232623_InitialIdentity";
        private readonly PostgresFixture _postgres;

        public PostgresMigrationTests(PostgresFixture postgres) => _postgres = postgres;

        [SkippableFact]
        public async Task FreshDatabaseHasSeparateSchemasHistoriesAndNoCrossSchemaKeys()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var connectionString = await _postgres.CreateMigratedDatabaseAsync();
            await using var connection = await OpenAsync(connectionString);

            var identityTables = await ListAsync(connection,
                "select table_name from information_schema.tables where table_schema = 'identity' order by table_name");
            Assert.Equal(
                new[] { "AccountInvitations", "AspNetRoleClaims", "AspNetRoles", "AspNetUserClaims", "AspNetUserLogins", "AspNetUserRoles", "AspNetUserTokens", "AspNetUsers", "DataProtectionKeys", "DeviceCredentials", "MfaLoginChallenges", "RefreshTokens", "SecurityAuditEvents", "__EFMigrationsHistory" }.Order(StringComparer.Ordinal),
                identityTables.Order(StringComparer.Ordinal));
            // 28 Domain tables + IdempotencyReceipts + FingerprintIdentificationSessions.
            Assert.Equal(30, await ScalarAsync<long>(connection,
                "select count(*) from information_schema.tables where table_schema = 'clinical' and table_name <> '__EFMigrationsHistory'"));
            Assert.Equal(0, await ScalarAsync<long>(connection,
                "select count(*) from information_schema.tables where table_schema not in ('identity', 'clinical', 'pg_catalog', 'information_schema')"));
            Assert.Equal(0, await ScalarAsync<long>(connection, """
                select count(*) from pg_constraint c
                join pg_class t on t.oid = c.conrelid join pg_namespace ns on ns.oid = t.relnamespace
                join pg_class r on r.oid = c.confrelid join pg_namespace nr on nr.oid = r.relnamespace
                where c.contype = 'f' and ns.nspname <> nr.nspname
                """));
            Assert.Equal(4, await ScalarAsync<long>(connection, "select count(*) from identity.\"__EFMigrationsHistory\""));
            // InitialClinical + EnforceSingleActiveTransferPerEncounter + AddClinicalIdempotencyReceipts
            // + AddFingerprintIdentificationSessions.
            Assert.Equal(4, await ScalarAsync<long>(connection, "select count(*) from clinical.\"__EFMigrationsHistory\""));
            Assert.Equal(0, await ScalarAsync<long>(connection,
                "select count(*) from pg_index i join pg_class c on c.oid = i.indexrelid join pg_namespace n on n.oid = c.relnamespace where n.nspname in ('identity', 'clinical') and not i.indisvalid"));

            await using var identity = PostgresFixture.Identity(connectionString);
            await using var clinical = PostgresFixture.Clinical(connectionString);
            Assert.False(identity.Database.HasPendingModelChanges());
            Assert.False(clinical.Database.HasPendingModelChanges());
        }

        [SkippableFact]
        public async Task UpgradeFromInitialIdentityKeepsExistingAccountsActive()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var connectionString = await _postgres.CreateDatabaseAsync();
            await using (var identity = PostgresFixture.Identity(connectionString))
            {
                await identity.GetService<IMigrator>().MigrateAsync(InitialIdentity);
            }

            await using var connection = await OpenAsync(connectionString);
            await ExecuteAsync(connection, """
                insert into identity."AspNetUsers" ("Id","UserName","NormalizedUserName","Email","NormalizedEmail","EmailConfirmed",
                    "PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount","SecurityStamp","ConcurrencyStamp")
                values (gen_random_uuid(),'legacy','LEGACY','legacy@sci-ph.test','LEGACY@SCI-PH.TEST',false,false,false,true,0,'s','c')
                """);

            await using (var identity = PostgresFixture.Identity(connectionString))
            {
                await identity.Database.MigrateAsync();
            }

            Assert.True(await ScalarAsync<bool>(connection, "select \"IsActive\" from identity.\"AspNetUsers\" where \"UserName\" = 'legacy'"));
            Assert.True(await ScalarAsync<bool>(connection,
                "select \"DeactivatedAtUtc\" is null and \"CreatedAtUtc\" > now() - interval '1 hour' from identity.\"AspNetUsers\" where \"UserName\" = 'legacy'"));
            Assert.Equal(4, await ScalarAsync<long>(connection, "select count(*) from identity.\"__EFMigrationsHistory\""));
        }

        [SkippableFact]
        public async Task ConstraintsAndUniqueIndexesRejectInvalidRows()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var connectionString = await _postgres.CreateMigratedDatabaseAsync();
            await using var connection = await OpenAsync(connectionString);
            var hash = new string('a', 64);
            const string user = "insert into identity.\"AspNetUsers\" (\"Id\",\"UserName\",\"NormalizedUserName\",\"Email\",\"NormalizedEmail\",\"EmailConfirmed\",\"PhoneNumberConfirmed\",\"TwoFactorEnabled\",\"LockoutEnabled\",\"AccessFailedCount\",\"IsActive\",\"CreatedAtUtc\") values ";
            await ExecuteAsync(connection, user + "('00000000-0000-0000-0000-000000000001','a','A','same@sci-ph.test','SAME@SCI-PH.TEST',false,false,false,true,0,true,now())");
            await ExecuteAsync(connection, $"insert into identity.\"RefreshTokens\" (\"Id\",\"UserId\",\"FamilyId\",\"TokenHash\",\"CreatedAtUtc\",\"ExpiresAtUtc\") values (gen_random_uuid(),'00000000-0000-0000-0000-000000000001',gen_random_uuid(),'{hash}',now(),now() + interval '1 day')");
            const string invitation = "insert into identity.\"AccountInvitations\" (\"Id\",\"SubjectType\",\"SubjectId\",\"Email\",\"IntendedRole\",\"TokenHash\",\"Status\",\"CreatedAtUtc\",\"ExpiresAtUtc\") values ";
            await ExecuteAsync(connection, invitation + $"(gen_random_uuid(),1,'00000000-0000-0000-0000-0000000000aa','p@sci-ph.test','Patient','{hash}',1,now(),now() + interval '1 day')");
            const string device = "insert into identity.\"DeviceCredentials\" (\"Id\",\"DeviceCode\",\"Name\",\"Scopes\",\"SecretHash\",\"Status\",\"CreatedAtUtc\") values ";
            await ExecuteAsync(connection, device + $"(gen_random_uuid(),'ESP32-001','Unit','Device.Heartbeat','{hash}',1,now())");

            await ExpectViolationAsync(connection, user + "('00000000-0000-0000-0000-000000000002','b','B','same@sci-ph.test','SAME@SCI-PH.TEST',false,false,false,true,0,true,now())", "EmailIndex");
            await ExpectViolationAsync(connection, user + "('00000000-0000-0000-0000-000000000003','c','C','c@sci-ph.test','C@SCI-PH.TEST',false,false,false,true,0,false,now())", "CK_AspNetUsers_Deactivation");
            await ExpectViolationAsync(connection, $"insert into identity.\"RefreshTokens\" (\"Id\",\"UserId\",\"FamilyId\",\"TokenHash\",\"CreatedAtUtc\",\"ExpiresAtUtc\") values (gen_random_uuid(),'00000000-0000-0000-0000-000000000001',gen_random_uuid(),'{hash}',now(),now() + interval '1 day')", "IX_RefreshTokens_TokenHash");
            await ExpectViolationAsync(connection, invitation + $"(gen_random_uuid(),1,'00000000-0000-0000-0000-0000000000bb','q@sci-ph.test','Patient','{hash}',1,now(),now() + interval '1 day')", "IX_AccountInvitations_TokenHash");
            await ExpectViolationAsync(connection, invitation + $"(gen_random_uuid(),1,'00000000-0000-0000-0000-0000000000aa','p@sci-ph.test','Patient','{new string('b', 64)}',1,now(),now() + interval '1 day')", "UX_AccountInvitations_PendingSubject");
            await ExpectViolationAsync(connection, invitation + $"(gen_random_uuid(),2,gen_random_uuid(),'s@sci-ph.test','Administrator','{new string('c', 64)}',1,now(),now() + interval '1 day')", "CK_AccountInvitations_Role");
            await ExpectViolationAsync(connection, device + $"(gen_random_uuid(),'ESP32-001','Other','Device.Heartbeat','{hash}',1,now())", "IX_DeviceCredentials_DeviceCode");
            await ExpectViolationAsync(connection, device + $"(gen_random_uuid(),'esp32 bad','Other','Device.Heartbeat','{hash}',1,now())", "CK_DeviceCredentials_Code");
            await ExpectViolationAsync(connection, device + $"(gen_random_uuid(),'ESP32-002','Other','Patients.Read','{hash}',1,now())", "CK_DeviceCredentials_Scopes");
            const string challenge = "insert into identity.\"MfaLoginChallenges\" (\"Id\",\"UserId\",\"ChallengeHash\",\"CreatedAtUtc\",\"ExpiresAtUtc\",\"ConsumedAtUtc\") values ";
            await ExecuteAsync(connection, challenge + $"(gen_random_uuid(),'00000000-0000-0000-0000-000000000001','{hash}',now(),now() + interval '5 minutes',null)");
            await ExpectViolationAsync(connection, challenge + $"(gen_random_uuid(),'00000000-0000-0000-0000-000000000001','{hash}',now(),now() + interval '5 minutes',null)", "IX_MfaLoginChallenges_ChallengeHash");
            await ExpectViolationAsync(connection, challenge + "(gen_random_uuid(),'00000000-0000-0000-0000-000000000001','raw-jti-not-a-hash',now(),now() + interval '5 minutes',null)", "CK_MfaLoginChallenges_Hash");
            await ExpectViolationAsync(connection, challenge + $"(gen_random_uuid(),'00000000-0000-0000-0000-000000000001','{new string('d', 64)}',now(),now() - interval '1 minute',null)", "CK_MfaLoginChallenges_Dates");
            await ExpectViolationAsync(connection, challenge + $"(gen_random_uuid(),'00000000-0000-0000-0000-000000000099','{new string('e', 64)}',now(),now() + interval '5 minutes',null)", "FK_MfaLoginChallenges_AspNetUsers_UserId");
            await ExpectViolationAsync(connection, "insert into identity.\"SecurityAuditEvents\" (\"Id\",\"EventType\",\"Outcome\",\"OccurredAtUtc\") values (gen_random_uuid(),1,2,now())", "CK_SecurityAuditEvents_Outcome");
        }

        private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            return connection;
        }

        private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        private static async Task<List<string>> ListAsync(NpgsqlConnection connection, string sql)
        {
            var values = new List<string>();
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                values.Add(reader.GetString(0));
            }

            return values;
        }

        private static async Task ExpectViolationAsync(NpgsqlConnection connection, string sql, string constraintName)
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, sql));
            Assert.Equal(constraintName, exception.ConstraintName);
        }
    }
}
