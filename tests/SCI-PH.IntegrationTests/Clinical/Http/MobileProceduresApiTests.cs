using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Persistence.Clinical.Idempotency;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Completed procedures on a real prehospital emergency through the Mobile BFF over PostgreSQL: the fixed catalog, one
    // procedure + its audit entry + the start of the encounter + the receipt in one transaction; replay, conflict and the
    // race of the same procedure without duplicates or 500s; access by assignment and permission; the encounter's
    // concurrency tokens against a close, a dispatch and an identification.
    [Collection(PostgresCollection.Name)]
    public sealed class MobileProceduresApiTests
    {
        private const string Emergencies = "/api/mobile/emergencies";
        private const string Catalog = "/api/mobile/catalogs/procedures";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly PostgresFixture _postgres;

        public MobileProceduresApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static DateTimeOffset NowToTheMillisecond()
        {
            var now = DateTimeOffset.UtcNow;
            return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        }

        private static string Url(Guid emergencyId) => $"{Emergencies}/{emergencyId}/procedures";

        private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Inv);

        private static string ProcedureJson(Guid key, string performedAt, string code = "OXYGEN-THERAPY", string? outcome = "SpO2 97 %") =>
            outcome is null
                ? $$"""{"clientRequestId":"{{key}}","procedureCode":"{{code}}","performedAt":"{{performedAt}}"}"""
                : $$"""{"clientRequestId":"{{key}}","procedureCode":"{{code}}","performedAt":"{{performedAt}}","outcome":"{{outcome}}"}""";

        private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, Guid emergencyId, string json) =>
            client.PostAsync(Url(emergencyId), new StringContent(json, Encoding.UTF8, "application/json"));

        private static async Task<PrehospitalEmergencySummary> OpenAsync(StaffSession paramedic, DateTimeOffset startedAt)
        {
            var response = await paramedic.Client.PostAsJsonAsync(Emergencies, new { clientRequestId = Guid.NewGuid(), startedAt, chiefComplaint = "Politraumatismo" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await response.ReadAsAsync<PrehospitalEmergencySummary>();
        }

        // InProgress through the real endpoint: a first reading of vital signs.
        private static async Task StartAsync(StaffSession paramedic, Guid emergencyId) =>
            Assert.Equal(HttpStatusCode.Created, (await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{emergencyId}/vital-signs",
                new { clientRequestId = Guid.NewGuid(), recordedAt = NowToTheMillisecond(), heartRate = 90 })).StatusCode);

        private static Task<int> ProceduresAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.ClinicalProcedures.CountAsync(p => p.ClinicalEncounterId == emergencyId));

        private static Task<int> ProcedureAuditsAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == emergencyId && a.EntityName == nameof(ClinicalProcedure)));

        private static Task<int> ReceiptsAsync(MobileEmergencyHost host) =>
            host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.Operation == PrehospitalEmergencyService.RecordProcedureOperation));

        private static Task<ClinicalEncounterStatus> StatusAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == emergencyId).Select(e => e.Status).SingleAsync());

        // A mobile user whose only permissions are the given ones (a test role), as active paramedic staff of the clinic.
        private static async Task<StaffSession> WithPermissionsAsync(MobileEmergencyHost host, SCI_PH.Domain.Entities.Organizations.Clinic clinic, params string[] permissions)
        {
            var role = $"Test-{Guid.NewGuid():N}"[..20];
            await using (var scope = host.Web.Services.CreateAsyncScope())
            {
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
                var created = new IdentityRole<Guid>(role);
                Assert.True((await roles.CreateAsync(created)).Succeeded);
                foreach (var permission in permissions)
                {
                    Assert.True((await roles.AddClaimAsync(created, new Claim(CustomClaimTypes.Permission, permission))).Succeeded);
                }
            }

            return await host.SignInAsync(role, [clinic]);
        }

        [SkippableFact]
        public async Task TheCatalogIsTheFixedServerCatalogBehindTheProcedurePermission()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var clinic = await host.Web.CreateClinicAsync();
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);

            var response = await paramedic.Client.GetAsync(Catalog);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var items = json.RootElement.EnumerateArray().ToList();
            Assert.Equal(PrehospitalProcedureCatalog.Entries.Select(e => e.Code), items.Select(i => i.GetProperty("code").GetString()));
            Assert.Equal(PrehospitalProcedureCatalog.Entries.Select(e => e.Name), items.Select(i => i.GetProperty("name").GetString()));
            Assert.All(items, i => Assert.Equal(["code", "name", "description"], i.EnumerateObject().Select(p => p.Name)));

            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Anonymous().GetAsync(Catalog)).StatusCode);
            var doctor = await host.SignInAsync(SecurityRoles.Doctor, [clinic], staffType: ClinicalStaffType.Physician);
            await AssertProblemAsync(await doctor.Client.GetAsync(Catalog), HttpStatusCode.Forbidden);
            var operateOnly = await WithPermissionsAsync(host, clinic, Permissions.Emergencies.Operate);
            await AssertProblemAsync(await operateOnly.Client.GetAsync(Catalog), HttpStatusCode.Forbidden);
            var notStaff = await host.SignInAsync(SecurityRoles.Paramedic, [clinic], asClinicalStaff: false);
            await AssertProblemAsync(await notStaff.Client.GetAsync(Catalog), HttpStatusCode.Forbidden);
        }

        // The first clinical record of the emergency: the procedure, its audit entry, Created -> InProgress and the receipt.
        [SkippableFact]
        public async Task AProcedureIsRecordedCompletedInOneTransactionAsTheFirstRecord()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            Assert.Equal(ClinicalEncounterStatus.Created, await StatusAsync(host, opened.Id));
            var performedAt = NowToTheMillisecond().AddMinutes(-1);
            var key = Guid.NewGuid();

            var response = await PostJsonAsync(paramedic.Client, opened.Id, ProcedureJson(key, Utc(performedAt), outcome: "  Sin complicaciones "));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Null(response.Headers.Location);
            var summary = await response.ReadAsAsync<EmergencyProcedureSummary>();
            var entry = PrehospitalProcedureCatalog.Find("OXYGEN-THERAPY")!;
            Assert.Equal((opened.Id, "OXYGEN-THERAPY", entry.Name, performedAt, "Sin complicaciones"),
                (summary.EncounterId, summary.Code, summary.Name, summary.PerformedAtUtc, summary.Outcome));

            var procedure = await host.Web.DbAsync(db => db.ClinicalProcedures.AsNoTracking().SingleAsync(p => p.ClinicalEncounterId == opened.Id));
            Assert.Equal(summary.Id, procedure.Id);
            Assert.Equal((entry.Name, entry.Description, PrehospitalProcedureCatalog.CodeSystem, "OXYGEN-THERAPY", ClinicalProcedureStatus.Completed),
                (procedure.Name, procedure.Description, procedure.CodeSystem, procedure.Code, procedure.Status));
            Assert.Equal((paramedic.StaffId, (DateTimeOffset?)performedAt, (DateTimeOffset?)performedAt, "Sin complicaciones"),
                (procedure.PerformedByStaffId, procedure.PerformedAtUtc, procedure.CompletedAtUtc, procedure.Outcome));

            var encounter = await host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == opened.Id));
            Assert.Equal((ClinicalEncounterStatus.InProgress, (Guid?)null, (Guid?)null), (encounter.Status, encounter.PatientId, encounter.ClinicId));
            var audit = await host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking()
                .SingleAsync(a => a.ClinicalEncounterId == opened.Id && a.EntityName == nameof(ClinicalProcedure)));
            Assert.Equal((AuditActionType.Create, procedure.Id.ToString(), paramedic.StaffId, (Guid?)null),
                (audit.Action, audit.EntityId, audit.ActorStaffId, audit.PatientId));
            var receipt = await host.Web.DbAsync(db => db.IdempotencyReceipts.AsNoTracking().SingleAsync(r => r.ClientRequestId == key));
            Assert.Equal((PrehospitalEmergencyService.RecordProcedureOperation, procedure.Id), (receipt.Operation, receipt.ResourceId));

            // A later procedure leaves the encounter as it is.
            var updatedAt = encounter.UpdatedAtUtc;
            Assert.Equal(HttpStatusCode.Created, (await PostJsonAsync(paramedic.Client, opened.Id, ProcedureJson(Guid.NewGuid(), Utc(NowToTheMillisecond()), "CPR", null))).StatusCode);
            var after = await host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == opened.Id));
            Assert.Equal((ClinicalEncounterStatus.InProgress, updatedAt), (after.Status, after.UpdatedAtUtc));
            Assert.Equal(2, await ProceduresAsync(host, opened.Id));
        }

        [SkippableFact]
        public async Task AnIdentifiedEmergencyRecordsItsPatientInTheAudit()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var nationalId = ClinicalApiHost.RandomNationalId();
            var patient = await host.Web.SeedPatientAsync(nationalId: nationalId);
            Assert.Equal(HttpStatusCode.OK, (await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{opened.Id}/identify/document",
                new { clientRequestId = Guid.NewGuid(), documentType = "NationalId", documentNumber = nationalId })).StatusCode);

            var response = await PostJsonAsync(paramedic.Client, opened.Id, ProcedureJson(Guid.NewGuid(), Utc(NowToTheMillisecond()), "IMMOBILIZATION", null));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Null((await response.ReadAsAsync<EmergencyProcedureSummary>()).Outcome);
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a =>
                a.ClinicalEncounterId == opened.Id && a.EntityName == nameof(ClinicalProcedure) && a.PatientId == patient.Id)));
        }

        // The wire contract: a catalog code, an explicit offset inside the window, an outcome within the Domain length, no
        // other field (the server owns name, description, code system, staff, times and status). Every rejection is a 400
        // ProblemDetails that writes nothing.
        [SkippableFact]
        public async Task InvalidProceduresAreRejectedWithoutWritingAnything()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var now = Utc(NowToTheMillisecond().AddMinutes(-1));
            string Json(string body) => $$"""{"clientRequestId":"{{Guid.NewGuid()}}",{{body}}}""";
            var at = $"\"performedAt\":\"{now}\"";
            var code = "\"procedureCode\":\"CPR\"";

            foreach (var (json, field) in new[]
            {
                (Json($"\"procedureCode\":\"\",{at}"), "procedureCode"),
                (Json($"\"procedureCode\":\"   \",{at}"), "procedureCode"),
                (Json($"\"procedureCode\":null,{at}"), null),                                                // MVC: non-nullable
                (Json($"\"procedureCode\":\"UNKNOWN\",{at}"), "procedureCode"),
                (Json($"\"procedureCode\":\"cpr\",{at}"), "procedureCode"),
                (Json($"\"procedureCode\":\"PROC-001\",{at}"), "procedureCode"),
                (Json(at), null),                                                                            // no code
                (Json(code), null),                                                                          // no time
                (Json($"{code},\"performedAt\":\"{NowToTheMillisecond().UtcDateTime:yyyy-MM-dd'T'HH:mm:ss}\""), null),  // no offset
                (Json($"{code},\"performedAt\":\"not a date\""), null),
                (Json($"{code},\"performedAt\":\"{Utc(NowToTheMillisecond().AddMinutes(-11))}\""), "performedAt"),       // before start
                (Json($"{code},\"performedAt\":\"{Utc(NowToTheMillisecond().AddMinutes(10))}\""), "performedAt"),        // beyond +5 min
                (Json($"{code},{at},\"outcome\":\"{new string('a', 4001)}\""), "outcome"),
                (Json($"{code},{at},\"name\":\"Otra cosa\""), null),                                         // unknown fields
                (Json($"{code},{at},\"description\":\"x\""), null),
                (Json($"{code},{at},\"codeSystem\":\"ICD-10\""), null),
                (Json($"{code},{at},\"notes\":\"x\""), null),
                (Json($"{code},{at},\"completedAt\":\"{now}\""), null),
                (Json($"{code},{at},\"status\":\"Completed\""), null),
                (Json($"{code},{at},\"staffId\":\"{Guid.NewGuid()}\""), null),
                (Json($"{code},{at},\"patientId\":\"{Guid.NewGuid()}\""), null),
                (Json($"{code},{at},\"clinicId\":\"{Guid.NewGuid()}\""), null),
                ($$"""{"clientRequestId":"{{Guid.Empty}}",{{code}},{{at}}}""", "clientRequestId")
            })
            {
                var response = await PostJsonAsync(paramedic.Client, opened.Id, json);
                Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{response.StatusCode}: {json}");
                var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest);
                if (field is not null)
                {
                    Assert.True(problem.TryGetProperty("field", out var actual) && actual.GetString() == field, $"{problem}: {json}");
                }
            }

            Assert.Equal(0, await ProceduresAsync(host, opened.Id));
            Assert.Equal(0, await ProcedureAuditsAsync(host, opened.Id));
            Assert.Equal(0, await ReceiptsAsync(host));
            Assert.Equal(ClinicalEncounterStatus.Created, await StatusAsync(host, opened.Id));
        }

        // Inside the window: the start of the emergency, now and now + 5 min (minus the network time), "Z" or an offset.
        [SkippableFact]
        public async Task PerformedAtGoesFromTheStartUpToFiveMinutesAheadWithZOrAnOffset()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var startedAt = NowToTheMillisecond().AddMinutes(-10);
            var opened = await OpenAsync(paramedic, startedAt);
            var offset = NowToTheMillisecond().AddMinutes(-2);

            foreach (var performedAt in new[]
            {
                Utc(startedAt),
                Utc(NowToTheMillisecond()),
                Utc(NowToTheMillisecond().AddMinutes(4.5)),
                offset.ToOffset(TimeSpan.FromHours(-4)).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", Inv)
            })
            {
                Assert.Equal(HttpStatusCode.Created, (await PostJsonAsync(paramedic.Client, opened.Id, ProcedureJson(Guid.NewGuid(), performedAt))).StatusCode);
            }

            var times = await host.Web.DbAsync(db => db.ClinicalProcedures.AsNoTracking().Where(p => p.ClinicalEncounterId == opened.Id)
                .Select(p => p.PerformedAtUtc!.Value).ToListAsync());
            Assert.Contains(startedAt, times);
            Assert.Contains(offset, times);
        }

        // Same key + same procedure ("Z" or offset, outcome with other spaces): 200 with the same procedure, nothing new.
        // Same key with another code, time, outcome or emergency: 409 and the original stays as it was.
        [SkippableFact]
        public async Task RetryingReplaysAndAReusedKeyWithOtherDataIsAConflict()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var other = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var performedAt = NowToTheMillisecond().AddMinutes(-1);
            var key = Guid.NewGuid();

            var first = await PostJsonAsync(paramedic.Client, opened.Id, ProcedureJson(key, Utc(performedAt)));
            var replay = await PostJsonAsync(paramedic.Client, opened.Id,
                ProcedureJson(key, performedAt.ToOffset(TimeSpan.FromHours(-4)).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", Inv), outcome: " SpO2 97 % "));

            Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK), (first.StatusCode, replay.StatusCode));
            var created = await first.ReadAsAsync<EmergencyProcedureSummary>();
            Assert.Equal(JsonSerializer.Serialize(created), JsonSerializer.Serialize(await replay.ReadAsAsync<EmergencyProcedureSummary>()));

            foreach (var (emergencyId, json) in new[]
            {
                (opened.Id, ProcedureJson(key, Utc(performedAt), "CPR")),
                (opened.Id, ProcedureJson(key, Utc(performedAt.AddSeconds(-1)))),
                (opened.Id, ProcedureJson(key, Utc(performedAt), outcome: "Otro")),
                (opened.Id, ProcedureJson(key, Utc(performedAt), outcome: null)),
                (other.Id, ProcedureJson(key, Utc(performedAt)))
            })
            {
                await AssertProblemAsync(await PostJsonAsync(paramedic.Client, emergencyId, json), HttpStatusCode.Conflict);
            }

            var stored = await host.Web.DbAsync(db => db.ClinicalProcedures.AsNoTracking().SingleAsync(p => p.ClinicalEncounterId == opened.Id));
            Assert.Equal((created.Id, "OXYGEN-THERAPY", "SpO2 97 %", (DateTimeOffset?)performedAt), (stored.Id, stored.Code, stored.Outcome, stored.PerformedAtUtc));
            Assert.Equal(1, await ProcedureAuditsAsync(host, opened.Id));
            Assert.Equal(0, await ProceduresAsync(host, other.Id));
            Assert.Equal(1, await ReceiptsAsync(host));
        }

        [SkippableFact]
        public async Task AConcurrentDoubleSubmitRecordsTheProcedureOnce()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);

            for (var round = 0; round < 5; round++)
            {
                var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
                var json = ProcedureJson(Guid.NewGuid(), Utc(NowToTheMillisecond().AddMinutes(-1)));
                using var start = new ManualResetEventSlim();
                Task<HttpResponseMessage> Submit() => Task.Run(async () => { start.Wait(); return await PostJsonAsync(paramedic.Client, opened.Id, json); });
                var tasks = new[] { Submit(), Submit() };
                start.Set();
                var responses = await Task.WhenAll(tasks);

                Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Created], responses.Select(r => r.StatusCode).Order());
                var ids = await Task.WhenAll(responses.Select(r => r.ReadAsAsync<EmergencyProcedureSummary>()));
                Assert.Equal(ids[0].Id, ids[1].Id);
                Assert.Equal(1, await ProceduresAsync(host, opened.Id));
                Assert.Equal(1, await ProcedureAuditsAsync(host, opened.Id));
            }

            Assert.Equal(5, await ReceiptsAsync(host));
        }

        // Deterministic race of the same procedure: another transaction holds its receipt and the start of the emergency,
        // uncommitted. The request waits on them and, when they commit, answers 200 from the receipt: no duplicate, no 500.
        [SkippableFact]
        public async Task LosingTheRaceToTheSameProcedureReturnsTheWinner()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var performedAt = NowToTheMillisecond().AddMinutes(-1);
            var key = Guid.NewGuid();
            var entry = PrehospitalProcedureCatalog.Find("OXYGEN-THERAPY")!;

            await using var other = PostgresFixture.Clinical(host.ConnectionString);
            await using var transaction = await other.Database.BeginTransactionAsync();
            var encounter = await other.ClinicalEncounters.SingleAsync(e => e.Id == opened.Id);
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, DateTimeOffset.UtcNow);
            var winner = ClinicalProcedure.RecordCompleted(opened.Id, entry.Name, paramedic.StaffId!.Value, performedAt, performedAt,
                "SpO2 97 %", entry.Description, PrehospitalProcedureCatalog.CodeSystem, entry.Code);
            other.ClinicalProcedures.Add(winner);
            other.IdempotencyReceipts.Add(IdempotencyReceiptEntry.From(new IdempotencyReceipt(
                new IdempotencyKey(paramedic.UserId, PrehospitalEmergencyService.RecordProcedureOperation, key),
                IdempotencyRequestHash.Compute(opened.Id.ToString("D"), entry.Code, IdempotencyRequestHash.Instant(performedAt), "SpO2 97 %"),
                winner.Id, DateTimeOffset.UtcNow)));
            await other.SaveChangesAsync();

            var request = PostJsonAsync(paramedic.Client, opened.Id, ProcedureJson(key, Utc(performedAt)));
            await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, request);
            await transaction.CommitAsync();
            var response = await request;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(winner.Id, (await response.ReadAsAsync<EmergencyProcedureSummary>()).Id);
            Assert.Equal(1, await ProceduresAsync(host, opened.Id));
            Assert.Equal(0, await ProcedureAuditsAsync(host, opened.Id));
            Assert.Equal(1, await ReceiptsAsync(host));
        }

        // EnsureEncounterUnchanged: another transaction closes, dispatches (InProgress -> Transferred) or identifies the
        // emergency while the procedure is being recorded. The procedure loses with a 409 and nothing of it is written.
        [SkippableTheory]
        [InlineData("close")]
        [InlineData("dispatch")]
        [InlineData("identify")]
        public async Task AProcedureLosesTheRaceAgainstAConcurrentChangeOfTheEmergency(string change)
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            await StartAsync(paramedic, opened.Id);
            var patient = await host.Web.SeedPatientAsync(nationalId: ClinicalApiHost.RandomNationalId());

            var (connection, competing) = change switch
            {
                "close" => await ClinicalRace.BeginCloseAsync(host.ConnectionString, opened.Id),
                "dispatch" => await ClinicalRace.BeginStatusChangeAsync(host.ConnectionString, opened.Id, from: 2, to: 3),
                _ => await BeginIdentifyAsync(host.ConnectionString, opened.Id, patient.Id, paramedic.StaffId!.Value)
            };

            await using (connection)
            {
                var request = PostJsonAsync(paramedic.Client, opened.Id, ProcedureJson(Guid.NewGuid(), Utc(NowToTheMillisecond())));
                await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, request);
                await competing.CommitAsync();

                await AssertProblemAsync(await request, HttpStatusCode.Conflict);
            }

            Assert.Equal(0, await ProceduresAsync(host, opened.Id));
            Assert.Equal(0, await ProcedureAuditsAsync(host, opened.Id));
            Assert.Equal(0, await ReceiptsAsync(host));
        }

        [SkippableFact]
        public async Task AClosedOrCancelledEmergencyRejectsTheProcedure()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);

            // Transferred without clinic is the way to the hospital and accepts records (MobileTransferApiTests).
            foreach (var target in new[] { ClinicalEncounterStatus.Closed, ClinicalEncounterStatus.Cancelled })
            {
                var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
                await host.Web.DbAsync(async db =>
                {
                    var encounter = await db.ClinicalEncounters.SingleAsync(e => e.Id == opened.Id);
                    if (target != ClinicalEncounterStatus.Cancelled)
                    {
                        encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, DateTimeOffset.UtcNow);
                    }

                    encounter.ChangeStatus(target, DateTimeOffset.UtcNow);
                    await db.SaveChangesAsync();
                });

                await AssertProblemAsync(await PostJsonAsync(paramedic.Client, opened.Id, ProcedureJson(Guid.NewGuid(), Utc(NowToTheMillisecond()))), HttpStatusCode.Conflict);
                Assert.Equal(0, await ProceduresAsync(host, opened.Id));
                Assert.Equal(target, await StatusAsync(host, opened.Id));
            }

            Assert.Equal(0, await ReceiptsAsync(host));
        }

        // Permissions are the authority: no token 401; without Emergencies.Operate (doctor, nurse: they have
        // Procedures.Write) or without Procedures.Write (a test role) 403; another paramedic 403; unknown or non-prehospital
        // encounter 404.
        [SkippableFact]
        public async Task OnlyTheAssignedParamedicWithBothPermissionsRecordsProcedures()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var clinic = await host.Web.CreateClinicAsync();
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            var otherParamedic = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            string Body() => ProcedureJson(Guid.NewGuid(), Utc(NowToTheMillisecond()));

            Assert.Equal(HttpStatusCode.Unauthorized, (await PostJsonAsync(host.Anonymous(), opened.Id, Body())).StatusCode);
            foreach (var (role, type) in new[]
            {
                (SecurityRoles.Doctor, ClinicalStaffType.Physician),
                (SecurityRoles.Nurse, ClinicalStaffType.Nurse),
                (SecurityRoles.Receptionist, ClinicalStaffType.OtherClinical)
            })
            {
                var user = await host.SignInAsync(role, [clinic], staffType: type);
                await AssertProblemAsync(await PostJsonAsync(user.Client, opened.Id, Body()), HttpStatusCode.Forbidden);
            }

            var withoutProcedures = await WithPermissionsAsync(host, clinic,
                Permissions.Emergencies.Operate, Permissions.Encounters.Read, Permissions.VitalSigns.Write);
            await AssertProblemAsync(await PostJsonAsync(withoutProcedures.Client, opened.Id, Body()), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await PostJsonAsync(otherParamedic.Client, opened.Id, Body()), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await PostJsonAsync(paramedic.Client, Guid.NewGuid(), Body()), HttpStatusCode.NotFound);

            var hospital = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Emergency, DateTimeOffset.UtcNow.AddMinutes(-30), clinic.Id);
            await host.Web.DbAsync(async db => { db.ClinicalEncounters.Add(hospital); await db.SaveChangesAsync(); });
            await AssertProblemAsync(await PostJsonAsync(paramedic.Client, hospital.Id, Body()), HttpStatusCode.NotFound);

            Assert.Equal(0, await ProceduresAsync(host, opened.Id));
            Assert.Equal(0, await ReceiptsAsync(host));
            Assert.Equal(HttpStatusCode.Created, (await PostJsonAsync(paramedic.Client, opened.Id, Body())).StatusCode);
        }

        // Another writer links the NN patient (PatientId, a concurrency token) in a transaction that the caller commits.
        private static async Task<(NpgsqlConnection, NpgsqlTransaction)> BeginIdentifyAsync(string connectionString, Guid encounterId, Guid patientId, Guid staffId)
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

        private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal((int)expected, problem.RootElement.GetProperty("status").GetInt32());
            return problem.RootElement.Clone();
        }
    }
}
