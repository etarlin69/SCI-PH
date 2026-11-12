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
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Persistence.Clinical.Idempotency;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Prehospital observations on a real emergency through the Mobile BFF over PostgreSQL: one note of type Prehospital by
    // the session's staff + its audit entry (without the content) + the start of the encounter + the receipt in one
    // transaction; replay, conflict and the race of the same note without duplicates or 500s; access by assignment and
    // permission; the encounter's concurrency tokens against a close, a dispatch and an identification; the Web note path
    // keeps its rules.
    [Collection(PostgresCollection.Name)]
    public sealed class MobileNotesApiTests
    {
        private const string Emergencies = "/api/mobile/emergencies";
        private const string Text = "Paciente consciente, orientado, sin dolor torácico.";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly PostgresFixture _postgres;

        public MobileNotesApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static DateTimeOffset NowToTheMillisecond()
        {
            var now = DateTimeOffset.UtcNow;
            return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        }

        private static string Url(Guid emergencyId) => $"{Emergencies}/{emergencyId}/notes";

        private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", Inv);

        private static string NoteJson(Guid key, string occurredAt, string content = Text) =>
            JsonSerializer.Serialize(new { clientRequestId = key, occurredAt, content });

        private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, Guid emergencyId, string json) =>
            client.PostAsync(Url(emergencyId), new StringContent(json, Encoding.UTF8, "application/json"));

        private static async Task<PrehospitalEmergencySummary> OpenAsync(StaffSession paramedic, DateTimeOffset startedAt)
        {
            var response = await paramedic.Client.PostAsJsonAsync(Emergencies, new { clientRequestId = Guid.NewGuid(), startedAt, chiefComplaint = "Síncope" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await response.ReadAsAsync<PrehospitalEmergencySummary>();
        }

        private static async Task StartAsync(StaffSession paramedic, Guid emergencyId) =>
            Assert.Equal(HttpStatusCode.Created, (await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{emergencyId}/vital-signs",
                new { clientRequestId = Guid.NewGuid(), recordedAt = NowToTheMillisecond(), heartRate = 90 })).StatusCode);

        private static Task<int> NotesAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.ClinicalNotes.CountAsync(n => n.ClinicalEncounterId == emergencyId));

        private static Task<int> AuditsAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == emergencyId && a.EntityName == nameof(ClinicalNote)));

        private static Task<int> ReceiptsAsync(MobileEmergencyHost host) =>
            host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.Operation == PrehospitalEmergencyService.RecordNoteOperation));

        private static Task<ClinicalEncounterStatus> StatusAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == emergencyId).Select(e => e.Status).SingleAsync());

        // A mobile user whose only permissions are the given ones (a test role), as active paramedic staff of the clinic.
        private static async Task<StaffSession> WithPermissionsAsync(MobileEmergencyHost host, Clinic clinic, params string[] permissions)
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

        // The first clinical record of the emergency: the note, its audit entry (without the text), Created -> InProgress and
        // the receipt.
        [SkippableFact]
        public async Task ANoteIsRecordedInOneTransactionAsTheFirstRecord()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            Assert.Equal(ClinicalEncounterStatus.Created, await StatusAsync(host, opened.Id));
            var occurredAt = NowToTheMillisecond().AddMinutes(-1);
            var key = Guid.NewGuid();

            var response = await PostJsonAsync(paramedic.Client, opened.Id, NoteJson(key, Utc(occurredAt), $"  {Text}\n"));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Null(response.Headers.Location);
            using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
            {
                Assert.Equal(["id", "encounterId", "type", "content", "occurredAtUtc"], body.RootElement.EnumerateObject().Select(p => p.Name));
                Assert.Equal(("Prehospital", Text), (body.RootElement.GetProperty("type").GetString(), body.RootElement.GetProperty("content").GetString()));
            }

            var note = await host.Web.DbAsync(db => db.ClinicalNotes.AsNoTracking().SingleAsync(n => n.ClinicalEncounterId == opened.Id));
            Assert.Equal((paramedic.StaffId!.Value, ClinicalNoteType.Prehospital, Text, occurredAt),
                (note.AuthorStaffId, note.Type, note.Content, note.OccurredAtUtc));
            Assert.Equal(TimeSpan.Zero, note.OccurredAtUtc.Offset);

            Assert.Equal(ClinicalEncounterStatus.InProgress, await StatusAsync(host, opened.Id));
            var audit = await host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking()
                .SingleAsync(a => a.ClinicalEncounterId == opened.Id && a.EntityName == nameof(ClinicalNote)));
            Assert.Equal((AuditActionType.Create, note.Id.ToString(), paramedic.StaffId, (Guid?)null, (string?)null),
                (audit.Action, audit.EntityId, audit.ActorStaffId, audit.PatientId, audit.ChangesJson));
            var receipt = await host.Web.DbAsync(db => db.IdempotencyReceipts.AsNoTracking().SingleAsync(r => r.ClientRequestId == key));
            Assert.Equal((PrehospitalEmergencyService.RecordNoteOperation, note.Id), (receipt.Operation, receipt.ResourceId));

            // Several notes: a later one leaves the encounter as it is.
            var updatedAt = await host.Web.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == opened.Id).Select(e => e.UpdatedAtUtc).SingleAsync());
            Assert.Equal(HttpStatusCode.Created, (await PostJsonAsync(paramedic.Client, opened.Id, NoteJson(Guid.NewGuid(), Utc(NowToTheMillisecond()), "Segunda observación."))).StatusCode);
            Assert.Equal(updatedAt, await host.Web.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == opened.Id).Select(e => e.UpdatedAtUtc).SingleAsync()));
            Assert.Equal(2, await NotesAsync(host, opened.Id));
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

            var response = await PostJsonAsync(paramedic.Client, opened.Id, NoteJson(Guid.NewGuid(), Utc(NowToTheMillisecond())));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a =>
                a.ClinicalEncounterId == opened.Id && a.EntityName == nameof(ClinicalNote) && a.PatientId == patient.Id && a.ChangesJson == null)));
        }

        // The wire contract: every rejection is a 400 ProblemDetails (with the field when the server owns the rule) that
        // writes nothing. 8000 characters exactly are accepted.
        [SkippableFact]
        public async Task InvalidNotesAreRejectedWithoutWritingAnything()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var now = Utc(NowToTheMillisecond().AddMinutes(-1));
            string With(string extra) => NoteJson(Guid.NewGuid(), now).TrimEnd('}') + "," + extra + "}";

            foreach (var (json, field) in new[]
            {
                ($$"""{"clientRequestId":"{{Guid.NewGuid()}}","occurredAt":"{{now}}","content":null}""", (string?)null),   // MVC: non-nullable
                ($$"""{"clientRequestId":"{{Guid.NewGuid()}}","occurredAt":"{{now}}"}""", null),                         // missing
                (NoteJson(Guid.NewGuid(), now, ""), "content"),
                (NoteJson(Guid.NewGuid(), now, "   \n\t "), "content"),
                (NoteJson(Guid.NewGuid(), now, new string('a', 8001)), "content"),
                (NoteJson(Guid.Empty, now), "clientRequestId"),
                (NoteJson(Guid.NewGuid(), $"{NowToTheMillisecond().UtcDateTime:yyyy-MM-dd'T'HH:mm:ss}"), null),           // no offset
                (NoteJson(Guid.NewGuid(), Utc(NowToTheMillisecond().AddMinutes(-11))), "occurredAt"),                    // before start
                (NoteJson(Guid.NewGuid(), Utc(NowToTheMillisecond().AddMinutes(10))), "occurredAt"),                     // beyond +5 min
                (With("\"type\":\"Prehospital\""), null),
                (With("\"noteType\":\"Evolution\""), null),
                (With($"\"authorStaffId\":\"{Guid.NewGuid()}\""), null),
                (With($"\"patientId\":\"{Guid.NewGuid()}\""), null),
                (With($"\"clinicId\":\"{Guid.NewGuid()}\""), null),
                (With($"\"createdAtUtc\":\"{now}\""), null),
                (With($"\"encounterId\":\"{opened.Id}\""), null)
            })
            {
                var response = await PostJsonAsync(paramedic.Client, opened.Id, json);
                Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{response.StatusCode}: {json[..Math.Min(json.Length, 200)]}");
                var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest);
                Assert.DoesNotContain("aaaaaaaa", problem.ToString());
                if (field is not null)
                {
                    Assert.True(problem.TryGetProperty("field", out var actual) && actual.GetString() == field, $"{problem}");
                }
            }

            Assert.Equal(0, await NotesAsync(host, opened.Id));
            Assert.Equal(0, await AuditsAsync(host, opened.Id));
            Assert.Equal(0, await ReceiptsAsync(host));
            Assert.Equal(ClinicalEncounterStatus.Created, await StatusAsync(host, opened.Id));

            Assert.Equal(HttpStatusCode.Created, (await PostJsonAsync(paramedic.Client, opened.Id, NoteJson(Guid.NewGuid(), now, new string('a', 8000)))).StatusCode);
            Assert.Equal(8000, await host.Web.DbAsync(db => db.ClinicalNotes.Where(n => n.ClinicalEncounterId == opened.Id).Select(n => n.Content.Length).SingleAsync()));
        }

        // Same key + same note (Z or offset, other surrounding spaces): 200 with the same note. Same key with another
        // content, time or emergency: 409 and the original stays as it was.
        [SkippableFact]
        public async Task RetryingReplaysAndAReusedKeyWithOtherDataIsAConflict()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var other = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var occurredAt = NowToTheMillisecond().AddMinutes(-1);
            var key = Guid.NewGuid();

            var first = await PostJsonAsync(paramedic.Client, opened.Id, NoteJson(key, Utc(occurredAt)));
            var replay = await PostJsonAsync(paramedic.Client, opened.Id,
                NoteJson(key, occurredAt.ToOffset(TimeSpan.FromHours(-4)).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", Inv), $" {Text}  "));

            Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK), (first.StatusCode, replay.StatusCode));
            var created = await first.ReadAsAsync<EmergencyNoteSummary>();
            Assert.Equal(JsonSerializer.Serialize(created), JsonSerializer.Serialize(await replay.ReadAsAsync<EmergencyNoteSummary>()));

            foreach (var (emergencyId, json) in new[]
            {
                (opened.Id, NoteJson(key, Utc(occurredAt), Text + " Editado.")),
                (opened.Id, NoteJson(key, Utc(occurredAt.AddSeconds(-1)))),
                (other.Id, NoteJson(key, Utc(occurredAt)))
            })
            {
                await AssertProblemAsync(await PostJsonAsync(paramedic.Client, emergencyId, json), HttpStatusCode.Conflict);
            }

            var stored = await host.Web.DbAsync(db => db.ClinicalNotes.AsNoTracking().SingleAsync(n => n.ClinicalEncounterId == opened.Id));
            Assert.Equal((created.Id, Text, occurredAt), (stored.Id, stored.Content, stored.OccurredAtUtc));
            Assert.Equal(1, await AuditsAsync(host, opened.Id));
            Assert.Equal(0, await NotesAsync(host, other.Id));
            Assert.Equal(1, await ReceiptsAsync(host));
        }

        [SkippableFact]
        public async Task AConcurrentDoubleSubmitRecordsTheNoteOnce()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);

            for (var round = 0; round < 5; round++)
            {
                var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
                var json = NoteJson(Guid.NewGuid(), Utc(NowToTheMillisecond().AddMinutes(-1)));
                using var start = new ManualResetEventSlim();
                Task<HttpResponseMessage> Submit() => Task.Run(async () => { start.Wait(); return await PostJsonAsync(paramedic.Client, opened.Id, json); });
                var tasks = new[] { Submit(), Submit() };
                start.Set();
                var responses = await Task.WhenAll(tasks);

                Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Created], responses.Select(r => r.StatusCode).Order());
                var ids = await Task.WhenAll(responses.Select(r => r.ReadAsAsync<EmergencyNoteSummary>()));
                Assert.Equal(ids[0].Id, ids[1].Id);
                Assert.Equal(1, await NotesAsync(host, opened.Id));
                Assert.Equal(1, await AuditsAsync(host, opened.Id));
            }

            Assert.Equal(5, await ReceiptsAsync(host));
        }

        // Deterministic race of the same note: another transaction holds its receipt and the start of the emergency,
        // uncommitted. The request waits on them and, when they commit, answers 200 from the receipt: no duplicate, no 500.
        [SkippableFact]
        public async Task LosingTheRaceToTheSameNoteReturnsTheWinner()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            var occurredAt = NowToTheMillisecond().AddMinutes(-1);
            var key = Guid.NewGuid();

            await using var other = PostgresFixture.Clinical(host.ConnectionString);
            await using var transaction = await other.Database.BeginTransactionAsync();
            var encounter = await other.ClinicalEncounters.SingleAsync(e => e.Id == opened.Id);
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, DateTimeOffset.UtcNow);
            var winner = ClinicalNote.Write(opened.Id, paramedic.StaffId!.Value, ClinicalNoteType.Prehospital, Text, occurredAt);
            other.ClinicalNotes.Add(winner);
            other.IdempotencyReceipts.Add(IdempotencyReceiptEntry.From(new IdempotencyReceipt(
                new IdempotencyKey(paramedic.UserId, PrehospitalEmergencyService.RecordNoteOperation, key),
                IdempotencyRequestHash.Compute(opened.Id.ToString("D"), IdempotencyRequestHash.Instant(occurredAt), Text),
                winner.Id, DateTimeOffset.UtcNow)));
            await other.SaveChangesAsync();

            var request = PostJsonAsync(paramedic.Client, opened.Id, NoteJson(key, Utc(occurredAt)));
            await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, request);
            await transaction.CommitAsync();
            var response = await request;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(winner.Id, (await response.ReadAsAsync<EmergencyNoteSummary>()).Id);
            Assert.Equal(1, await NotesAsync(host, opened.Id));
            Assert.Equal(0, await AuditsAsync(host, opened.Id));
            Assert.Equal(1, await ReceiptsAsync(host));
        }

        // EnsureEncounterUnchanged: another transaction closes, dispatches (InProgress -> Transferred) or identifies the
        // emergency while the note is being recorded. The note loses with a 409 and nothing of it is written.
        [SkippableTheory]
        [InlineData("close")]
        [InlineData("dispatch")]
        [InlineData("identify")]
        public async Task ANoteLosesTheRaceAgainstAConcurrentChangeOfTheEmergency(string change)
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
                var request = PostJsonAsync(paramedic.Client, opened.Id, NoteJson(Guid.NewGuid(), Utc(NowToTheMillisecond())));
                await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, request);
                await competing.CommitAsync();

                await AssertProblemAsync(await request, HttpStatusCode.Conflict);
            }

            Assert.Equal(0, await NotesAsync(host, opened.Id));
            Assert.Equal(0, await AuditsAsync(host, opened.Id));
            Assert.Equal(0, await ReceiptsAsync(host));
        }

        [SkippableFact]
        public async Task AClosedOrCancelledEmergencyRejectsTheNote()
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

                await AssertProblemAsync(await PostJsonAsync(paramedic.Client, opened.Id, NoteJson(Guid.NewGuid(), Utc(NowToTheMillisecond()))), HttpStatusCode.Conflict);
                Assert.Equal(0, await NotesAsync(host, opened.Id));
                Assert.Equal(target, await StatusAsync(host, opened.Id));
            }

            Assert.Equal(0, await ReceiptsAsync(host));
        }

        // Permissions are the authority: no token 401; without Emergencies.Operate (doctor, nurse: they have
        // ClinicalNotes.Write) or without ClinicalNotes.Write (a test role) 403; another paramedic 403; unknown or
        // non-prehospital encounter 404.
        [SkippableFact]
        public async Task OnlyTheAssignedParamedicWithBothPermissionsRecordsNotes()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var clinic = await host.Web.CreateClinicAsync();
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            var otherParamedic = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            var opened = await OpenAsync(paramedic, NowToTheMillisecond().AddMinutes(-10));
            string Body() => NoteJson(Guid.NewGuid(), Utc(NowToTheMillisecond()));

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

            var withoutWrite = await WithPermissionsAsync(host, clinic, Permissions.Emergencies.Operate, Permissions.Encounters.Read);
            await AssertProblemAsync(await PostJsonAsync(withoutWrite.Client, opened.Id, Body()), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await PostJsonAsync(otherParamedic.Client, opened.Id, Body()), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await PostJsonAsync(paramedic.Client, Guid.NewGuid(), Body()), HttpStatusCode.NotFound);

            var hospital = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Emergency, DateTimeOffset.UtcNow.AddMinutes(-30), clinic.Id);
            await host.Web.DbAsync(async db => { db.ClinicalEncounters.Add(hospital); await db.SaveChangesAsync(); });
            await AssertProblemAsync(await PostJsonAsync(paramedic.Client, hospital.Id, Body()), HttpStatusCode.NotFound);

            Assert.Equal(0, await NotesAsync(host, opened.Id));
            Assert.Equal(0, await ReceiptsAsync(host));
            Assert.Equal(HttpStatusCode.Created, (await PostJsonAsync(paramedic.Client, opened.Id, Body())).StatusCode);
        }

        // Regression: the Web note keeps the clinic rule and rejects a future occurredAtUtc.
        [SkippableFact]
        public async Task TheWebNoteKeepsTheClinicAndNoFutureRules()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinicA = await host.CreateClinicAsync("Hospital A");
            var clinicB = await host.CreateClinicAsync("Hospital B");
            var nurseA = await host.SignInAsync(SecurityRoles.Nurse, [clinicA], staffType: ClinicalStaffType.Nurse);
            var nurseB = await host.SignInAsync(SecurityRoles.Nurse, [clinicB], staffType: ClinicalStaffType.Nurse);
            var encounter = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Emergency, DateTimeOffset.UtcNow.AddMinutes(-30), clinicA.Id);
            await host.DbAsync(async db => { db.ClinicalEncounters.Add(encounter); await db.SaveChangesAsync(); });
            object Body(DateTimeOffset occurredAtUtc) => new { type = "Evolution", content = Text, occurredAtUtc };
            var url = $"/api/web/encounters/{encounter.Id}/notes";

            Assert.Equal(HttpStatusCode.Created, (await nurseA.Client.PostAsJsonAsync(url, Body(DateTimeOffset.UtcNow.AddMinutes(-1)))).StatusCode);
            await AssertProblemAsync(await nurseB.Client.PostAsJsonAsync(url, Body(DateTimeOffset.UtcNow.AddMinutes(-1))), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await nurseA.Client.PostAsJsonAsync(url, Body(DateTimeOffset.UtcNow.AddMinutes(3))), HttpStatusCode.BadRequest);
        }

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
