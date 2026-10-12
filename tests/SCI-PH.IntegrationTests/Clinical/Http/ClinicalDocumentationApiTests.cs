using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Clinical notes, diagnoses and close through the real Web BFF over PostgreSQL, including the race between closing
    // an encounter and writing a clinical record into it.
    [Collection(PostgresCollection.Name)]
    public sealed class ClinicalDocumentationApiTests
    {
        private const string Encounters = "/api/web/encounters";
        private readonly PostgresFixture _postgres;

        public ClinicalDocumentationApiTests(PostgresFixture postgres) => _postgres = postgres;

        private sealed record Setup(ClinicalApiHost Host, StaffSession Doctor, StaffSession Nurse, StaffSession Reception, EncounterSummary Encounter);

        // An encounter already in progress (first vital sign by the nurse), with a doctor, a nurse and a receptionist.
        private async Task<Setup> InProgressAsync()
        {
            var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinic = await host.CreateClinicAsync();
            var doctor = await host.SignInAsync(SecurityRoles.Doctor, [clinic], staffType: ClinicalStaffType.Physician);
            var nurse = await host.SignInAsync(SecurityRoles.Nurse, [clinic], staffType: ClinicalStaffType.Nurse);
            var reception = await host.SignInAsync(SecurityRoles.Receptionist, [clinic]);
            var created = await reception.Client.PostAsJsonAsync(Encounters, new { type = "Emergency", patientId = (await host.SeedPatientAsync()).Id });
            var encounter = await created.ReadAsAsync<EncounterSummary>();
            var vital = await nurse.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/vital-signs",
                new { type = "HeartRate", value = 90, measuredAtUtc = DateTimeOffset.UtcNow });
            Assert.Equal(HttpStatusCode.Created, vital.StatusCode);
            return new Setup(host, doctor, nurse, reception, encounter);
        }

        private static object NoteBody(string type = "Evolution") =>
            new { type, content = "Evolución favorable (dato de prueba).", occurredAtUtc = DateTimeOffset.UtcNow };

        [SkippableFact]
        public async Task NotesAndDiagnosesArePersistedAndListed()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;

            var nurseNote = await s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{id}/notes", NoteBody("Assessment"));
            var doctorNote = await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/notes", NoteBody());
            var primary = await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/diagnoses",
                new { description = "Neumonía adquirida en la comunidad", type = "Definitive", isPrimary = true, codeSystem = "ICD-10", code = "J18.9" });
            var secondary = await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/diagnoses",
                new { description = "Deshidratación leve", type = "Presumptive" });

            Assert.All(new[] { nurseNote, doctorNote, primary, secondary }, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
            Assert.EndsWith($"{Encounters}/{id}/notes", nurseNote.Headers.Location!.ToString());
            var notes = await (await s.Doctor.Client.GetAsync($"{Encounters}/{id}/notes")).ReadAsAsync<List<ClinicalNoteSummary>>();
            var diagnoses = await (await s.Nurse.Client.GetAsync($"{Encounters}/{id}/diagnoses")).ReadAsAsync<List<DiagnosisSummary>>();
            Assert.Equal(2, notes.Count);
            // The author comes from the session of each caller, never from the request.
            Assert.Equal(new[] { s.Doctor.StaffId!.Value, s.Nurse.StaffId!.Value }.Order(), notes.Select(n => n.AuthorStaffId).Order());
            Assert.All(notes, n => Assert.False(string.IsNullOrWhiteSpace(n.AuthorDisplayName)));
            Assert.Equal(2, diagnoses.Count);
            Assert.True(diagnoses[0].IsPrimary);
            Assert.Equal("J18.9", diagnoses[0].Code);
            Assert.NotNull(diagnoses[0].ConfirmedAtUtc);
            Assert.Equal(4, await s.Host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a =>
                a.ClinicalEncounterId == id && (a.EntityName == "ClinicalNote" || a.EntityName == "Diagnosis"))));
        }

        [SkippableFact]
        public async Task CloseEndsTheEncounterAndNothingCanBeWrittenAfterwards()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;

            var close = await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/close", new { dischargeNote = "Alta a domicilio (dato de prueba)." });

            Assert.Equal(HttpStatusCode.NoContent, close.StatusCode);
            var stored = await s.Host.DbAsync(db => db.ClinicalEncounters.SingleAsync(e => e.Id == id));
            Assert.Equal(ClinicalEncounterStatus.Closed, stored.Status);
            Assert.NotNull(stored.EndedAtUtc);
            Assert.Equal("Discharge", (await (await s.Doctor.Client.GetAsync($"{Encounters}/{id}/notes")).ReadAsAsync<List<ClinicalNoteSummary>>()).Single().Type.ToString());
            Assert.Equal(1, await s.Host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == id && a.Action == AuditActionType.Update)));

            await AssertConflictAsync(await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/close", new { }));
            await AssertConflictAsync(await s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{id}/notes", NoteBody()));
            await AssertConflictAsync(await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/diagnoses", new { description = "x", type = "Presumptive" }));
            await AssertConflictAsync(await s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{id}/triages", new { priority = "Urgent" }));
            await AssertConflictAsync(await s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{id}/vital-signs",
                new { type = "HeartRate", value = 80, measuredAtUtc = DateTimeOffset.UtcNow }));
            // Closed encounters leave the open list but stay readable.
            Assert.DoesNotContain(await (await s.Doctor.Client.GetAsync(Encounters)).ReadAsAsync<List<EncounterSummary>>(), e => e.Id == id);
            Assert.Equal(ClinicalEncounterStatus.Closed, (await (await s.Doctor.Client.GetAsync($"{Encounters}/{id}")).ReadAsAsync<EncounterSummary>()).Status);
        }

        [SkippableFact]
        public async Task CreatedEncounterCannotBeClosedAndUnknownOneIsNotFound()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var created = await (await s.Reception.Client.PostAsJsonAsync(Encounters, new { type = "Emergency" })).ReadAsAsync<EncounterSummary>();

            await AssertConflictAsync(await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{created.Id}/close", new { }));
            await AssertStatusAsync(await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{Guid.NewGuid()}/close", new { }), HttpStatusCode.NotFound);
            await AssertStatusAsync(await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{Guid.NewGuid()}/notes", NoteBody()), HttpStatusCode.NotFound);
            await AssertStatusAsync(await s.Doctor.Client.GetAsync($"{Encounters}/{Guid.NewGuid()}/diagnoses"), HttpStatusCode.NotFound);
        }

        [SkippableFact]
        public async Task PermissionsFollowTheRealRoleMatrix()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;
            var anonymous = s.Host.Anonymous();

            await AssertStatusAsync(await anonymous.GetAsync($"{Encounters}/{id}/notes"), HttpStatusCode.Unauthorized);
            await AssertStatusAsync(await anonymous.PostAsJsonAsync($"{Encounters}/{id}/close", new { }), HttpStatusCode.Unauthorized);
            // Nurse: writes notes, but has no Diagnoses.Write nor Encounters.Close.
            Assert.Equal(HttpStatusCode.Created, (await s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{id}/notes", NoteBody())).StatusCode);
            await AssertStatusAsync(await s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{id}/diagnoses", new { description = "x", type = "Presumptive" }), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{id}/close", new { }), HttpStatusCode.Forbidden);
            // Receptionist: no clinical access at all.
            await AssertStatusAsync(await s.Reception.Client.GetAsync($"{Encounters}/{id}/notes"), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await s.Reception.Client.GetAsync($"{Encounters}/{id}/diagnoses"), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await s.Reception.Client.PostAsJsonAsync($"{Encounters}/{id}/notes", NoteBody()), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await s.Reception.Client.PostAsJsonAsync($"{Encounters}/{id}/close", new { }), HttpStatusCode.Forbidden);
            Assert.Equal(ClinicalEncounterStatus.InProgress,
                await s.Host.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == id).Select(e => e.Status).SingleAsync()));
        }

        [SkippableFact]
        public async Task ValidationEnumsAndClinicIsolation()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;
            var otherClinic = await s.Host.CreateClinicAsync("Hospital ajeno");
            var foreignDoctor = await s.Host.SignInAsync(SecurityRoles.Doctor, [otherClinic], staffType: ClinicalStaffType.Physician);

            await AssertStatusAsync(await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/notes", NoteBody("Discharge")), HttpStatusCode.BadRequest);
            await AssertStatusAsync(await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/notes",
                new { type = "Evolution", content = "x", occurredAtUtc = DateTimeOffset.UtcNow.AddHours(1) }), HttpStatusCode.BadRequest);
            await AssertStatusAsync(await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/notes",
                new { type = 2, content = "x", occurredAtUtc = DateTimeOffset.UtcNow }), HttpStatusCode.BadRequest);
            await AssertStatusAsync(await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/notes",
                new { type = "Evolution", content = "x", occurredAtUtc = DateTimeOffset.UtcNow, authorStaffId = Guid.NewGuid() }), HttpStatusCode.BadRequest);
            await AssertStatusAsync(await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/diagnoses",
                new { description = "Cefalea", type = "Presumptive", code = "R51" }), HttpStatusCode.BadRequest);
            await AssertStatusAsync(await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/diagnoses",
                new { description = "Cefalea", type = "Presumptive", recordedByStaffId = Guid.NewGuid() }), HttpStatusCode.BadRequest);

            await s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/diagnoses", new { description = "Cefalea tensional", type = "Presumptive" });
            using var diagnoses = JsonDocument.Parse(await s.Doctor.Client.GetStringAsync($"{Encounters}/{id}/diagnoses"));
            Assert.Equal("Presumptive", diagnoses.RootElement[0].GetProperty("type").GetString());

            await AssertStatusAsync(await foreignDoctor.Client.GetAsync($"{Encounters}/{id}/notes"), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await foreignDoctor.Client.PostAsJsonAsync($"{Encounters}/{id}/diagnoses", new { description = "x", type = "Presumptive" }), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await foreignDoctor.Client.PostAsJsonAsync($"{Encounters}/{id}/close", new { }), HttpStatusCode.Forbidden);
        }

        // Another transaction closes the encounter and stays open while the clinical write is processed. The request
        // reads the committed state (InProgress), passes every check, then blocks on the encounter row. Once the close
        // commits, its token check matches no row: the request must fail with 409 and leave nothing behind.
        [SkippableTheory]
        [InlineData("triages")]
        [InlineData("vital-signs")]
        [InlineData("notes")]
        [InlineData("diagnoses")]
        public async Task AConcurrentCloseWinsOverAClinicalWrite(string record)
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;
            var before = await CountRecordsAsync(s.Host, id);

            await using var connection = new NpgsqlConnection(s.Host.ConnectionString);
            await connection.OpenAsync();
            await using var close = await connection.BeginTransactionAsync();
            await using (var update = new NpgsqlCommand(
                """UPDATE clinical."ClinicalEncounters" SET "Status" = 6, "EndedAtUtc" = GREATEST(now(), "StartedAtUtc") WHERE "Id" = @id AND "Status" = 2""", connection, close))
            {
                update.Parameters.AddWithValue("id", id);
                Assert.Equal(1, await update.ExecuteNonQueryAsync());
            }

            var write = record switch
            {
                "triages" => s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/triages", new { priority = "Urgent" }),
                "vital-signs" => s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/vital-signs",
                    new { type = "HeartRate", value = 88, measuredAtUtc = DateTimeOffset.UtcNow }),
                "notes" => s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/notes", NoteBody()),
                _ => s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{id}/diagnoses", new { description = "Sepsis", type = "Presumptive" })
            };

            await WaitUntilBlockedOnLockAsync(s.Host.ConnectionString, write);
            await close.CommitAsync();
            var response = await write;

            await AssertConflictAsync(response);
            Assert.Equal(before, await CountRecordsAsync(s.Host, id));
            Assert.Equal(ClinicalEncounterStatus.Closed,
                await s.Host.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == id).Select(e => e.Status).SingleAsync()));
        }

        // Real parallel requests, several rounds: whatever the order, the API answers 201/204 or 409 (never 500) and
        // no record is ever stored after the close that won.
        [SkippableFact]
        public async Task ParallelCloseAndWritesNeverFailUnexpectedly()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;

            for (var round = 0; round < 4; round++)
            {
                var encounter = await (await s.Reception.Client.PostAsJsonAsync(Encounters,
                    new { type = "Emergency", patientId = (await s.Host.SeedPatientAsync()).Id })).ReadAsAsync<EncounterSummary>();
                Assert.Equal(HttpStatusCode.Created, (await s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/vital-signs",
                    new { type = "HeartRate", value = 90, measuredAtUtc = DateTimeOffset.UtcNow })).StatusCode);

                using var start = new ManualResetEventSlim();
                Task<HttpResponseMessage> Run(Func<Task<HttpResponseMessage>> call) => Task.Run(async () => { start.Wait(); return await call(); });
                var tasks = new[]
                {
                    Run(() => s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/close", new { })),
                    Run(() => s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/triages", new { priority = "Standard" })),
                    Run(() => s.Nurse.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/notes", NoteBody())),
                    Run(() => s.Doctor.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/diagnoses", new { description = "Dolor abdominal", type = "Presumptive" }))
                };
                start.Set();
                var responses = await Task.WhenAll(tasks);

                Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.NoContent, HttpStatusCode.Conflict }));
                Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
                Assert.Equal(ClinicalEncounterStatus.Closed,
                    await s.Host.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == encounter.Id).Select(e => e.Status).SingleAsync()));
                // Each accepted write was committed before the close (its UPDATE matched InProgress); a 409 left nothing.
                Assert.Equal(responses.Skip(1).Count(r => r.StatusCode == HttpStatusCode.Created),
                    await CountRecordsAsync(s.Host, encounter.Id) - 1);
            }
        }

        private static async Task WaitUntilBlockedOnLockAsync(string connectionString, Task<HttpResponseMessage> request)
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

        private static Task<int> CountRecordsAsync(ClinicalApiHost host, Guid encounterId) =>
            host.DbAsync(async db =>
                await db.Triages.CountAsync(t => t.ClinicalEncounterId == encounterId)
                + await db.VitalSigns.CountAsync(v => v.ClinicalEncounterId == encounterId)
                + await db.ClinicalNotes.CountAsync(n => n.ClinicalEncounterId == encounterId)
                + await db.Diagnoses.CountAsync(d => d.ClinicalEncounterId == encounterId));

        private static Task AssertConflictAsync(HttpResponseMessage response) => AssertStatusAsync(response, HttpStatusCode.Conflict);

        private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal((int)expected, problem.RootElement.GetProperty("status").GetInt32());
        }
    }
}
