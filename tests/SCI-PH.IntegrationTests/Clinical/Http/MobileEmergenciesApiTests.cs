using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Transport;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;
using SCI_PH.Persistence.Clinical.Idempotency;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Real prehospital emergency through the Mobile BFF over PostgreSQL: a clinic-less Prehospital NN encounter, the
    // paramedic's assignment, the audit entry and the idempotency receipt in one transaction; replay, conflict and the
    // concurrent double submit protected by the receipts' unique key; access by assignment, never by the base clinic.
    [Collection(PostgresCollection.Name)]
    public sealed class MobileEmergenciesApiTests
    {
        private const string Emergencies = "/api/mobile/emergencies";
        private const string WebEncounters = "/api/web/encounters";
        private readonly PostgresFixture _postgres;

        public MobileEmergenciesApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static DateTimeOffset NowToTheMillisecond()
        {
            var now = DateTimeOffset.UtcNow;
            return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        }

        private static object Body(Guid clientRequestId, DateTimeOffset startedAt, string chiefComplaint = "Dolor torácico") =>
            new { clientRequestId, startedAt, chiefComplaint };

        [SkippableFact]
        public async Task AParamedicOpensARealPrehospitalEmergency()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base de ambulancias");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var startedAt = NowToTheMillisecond().AddMinutes(-7);
            var key = Guid.NewGuid();

            var response = await paramedic.Client.PostAsJsonAsync(Emergencies, Body(key, startedAt, "  Disnea súbita "));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.ReadAsAsync<PrehospitalEmergencySummary>();
            Assert.Equal($"/api/mobile/emergencies/{created.Id}", response.Headers.Location!.AbsolutePath, ignoreCase: true);
            Assert.Matches("^NN-[0-9A-F]{32}$", created.TemporaryPatientCode);
            Assert.Equal((ClinicalEncounterStatus.Created, startedAt, "Disnea súbita"), (created.Status, created.StartedAtUtc, created.ChiefComplaint));
            Assert.Null(created.Patient);
            // The JSON contract: enums as text, patient explicitly null, no clinic or staff in the response.
            using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
            {
                Assert.Equal("Created", json.RootElement.GetProperty("status").GetString());
                Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("patient").ValueKind);
                Assert.False(json.RootElement.TryGetProperty("clinicId", out _));
            }

            var encounter = await host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == created.Id));
            Assert.Equal(ClinicalEncounterType.Prehospital, encounter.Type);
            Assert.Null(encounter.ClinicId);
            Assert.Null(encounter.PatientId);
            Assert.Equal(created.TemporaryPatientCode, encounter.TemporaryPatientCode!.Value);
            Assert.Equal("Disnea súbita", encounter.Reason);
            Assert.Equal(startedAt, encounter.StartedAtUtc);

            var assignment = await host.Web.DbAsync(db => db.EncounterStaffAssignments.AsNoTracking().SingleAsync(a => a.ClinicalEncounterId == created.Id));
            Assert.Equal((paramedic.StaffId!.Value, EncounterStaffFunction.Paramedic, (DateTimeOffset?)null),
                (assignment.ClinicalStaffId, assignment.Function, assignment.UnassignedAtUtc));

            var audit = await host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking().SingleAsync(a => a.ClinicalEncounterId == created.Id));
            Assert.Equal((AuditActionType.Create, nameof(ClinicalEncounter), paramedic.UserId, paramedic.StaffId),
                (audit.Action, audit.EntityName, audit.ActorIdentityUserId, audit.ActorStaffId));

            var receipt = await host.Web.DbAsync(db => db.IdempotencyReceipts.AsNoTracking().SingleAsync(r => r.ResourceId == created.Id));
            Assert.Equal((paramedic.UserId, PrehospitalEmergencyService.OpenOperation, key), (receipt.ActorUserId, receipt.Operation, receipt.ClientRequestId));
            Assert.Equal(IdempotencyRequestHash.Compute(IdempotencyRequestHash.Instant(startedAt), "Disnea súbita"), receipt.RequestHash);

            var read = await paramedic.Client.GetAsync($"{Emergencies}/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal(created, await read.ReadAsAsync<PrehospitalEmergencySummary>());
        }

        [SkippableFact]
        public async Task RetryingReplaysAndAReusedKeyWithOtherDataIsAConflict()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var key = Guid.NewGuid();
            var startedAt = NowToTheMillisecond().AddMinutes(-3);

            var first = await paramedic.Client.PostAsJsonAsync(Emergencies, Body(key, startedAt));
            var replay = await paramedic.Client.PostAsJsonAsync(Emergencies, Body(key, startedAt));
            var conflict = await paramedic.Client.PostAsJsonAsync(Emergencies, Body(key, startedAt, "Otra cosa"));
            var otherStart = await paramedic.Client.PostAsJsonAsync(Emergencies, Body(key, startedAt.AddSeconds(1)));

            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal((await first.ReadAsAsync<PrehospitalEmergencySummary>()).Id, (await replay.ReadAsAsync<PrehospitalEmergencySummary>()).Id);
            await AssertStatusAsync(conflict, HttpStatusCode.Conflict);
            await AssertStatusAsync(otherStart, HttpStatusCode.Conflict);
            await AssertSingleEmergencyAsync(host, paramedic.UserId);
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ActorIdentityUserId == paramedic.UserId)));
        }

        // Both requests start together; whichever commits first gets 201, the other 200 with the same emergency.
        [SkippableFact]
        public async Task AConcurrentDoubleSubmitOpensOneEmergency()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var startedAt = NowToTheMillisecond().AddMinutes(-1);

            for (var round = 0; round < 5; round++)
            {
                var body = Body(Guid.NewGuid(), startedAt);
                using var start = new ManualResetEventSlim();
                Task<HttpResponseMessage> Submit() => Task.Run(async () => { start.Wait(); return await paramedic.Client.PostAsJsonAsync(Emergencies, body); });
                var tasks = new[] { Submit(), Submit() };
                start.Set();
                var responses = await Task.WhenAll(tasks);

                Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Created], responses.Select(r => r.StatusCode).Order());
                var ids = await Task.WhenAll(responses.Select(r => r.ReadAsAsync<PrehospitalEmergencySummary>()));
                Assert.Equal(ids[0].Id, ids[1].Id);
            }

            Assert.Equal(5, await host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.ActorUserId == paramedic.UserId)));
            Assert.Equal(5, await host.Web.DbAsync(db => db.EncounterStaffAssignments.CountAsync(a => a.ClinicalStaffId == paramedic.StaffId)));
            Assert.Equal(5, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ActorIdentityUserId == paramedic.UserId)));
            Assert.Equal(5, await host.Web.DbAsync(db => db.ClinicalEncounters.CountAsync(e => e.Type == ClinicalEncounterType.Prehospital)));
        }

        // Deterministic race: another transaction holds an uncommitted receipt with the same key, so the request does not
        // see it, writes everything and then waits on the unique key. When the other transaction commits, the request's
        // whole transaction fails on the unique key and it answers 200 with the winner's emergency: no duplicate, no 500.
        [SkippableFact]
        public async Task LosingTheRaceOnTheUniqueKeyReturnsTheWinner()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var key = Guid.NewGuid();
            var startedAt = NowToTheMillisecond().AddMinutes(-2);
            const string complaint = "Dolor torácico";

            await using var other = PostgresFixture.Clinical(host.ConnectionString);
            await using var transaction = await other.Database.BeginTransactionAsync();
            var winner = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Prehospital, startedAt, reason: complaint);
            other.ClinicalEncounters.Add(winner);
            other.EncounterStaffAssignments.Add(EncounterStaffAssignment.Assign(winner.Id, paramedic.StaffId!.Value, EncounterStaffFunction.Paramedic, startedAt));
            other.IdempotencyReceipts.Add(IdempotencyReceiptEntry.From(new IdempotencyReceipt(
                new IdempotencyKey(paramedic.UserId, PrehospitalEmergencyService.OpenOperation, key),
                IdempotencyRequestHash.Compute(IdempotencyRequestHash.Instant(startedAt), complaint), winner.Id, startedAt)));
            await other.SaveChangesAsync();

            var request = paramedic.Client.PostAsJsonAsync(Emergencies, Body(key, startedAt, complaint));
            await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, request);
            await transaction.CommitAsync();
            var response = await request;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(winner.Id, (await response.ReadAsAsync<PrehospitalEmergencySummary>()).Id);
            await AssertSingleEmergencyAsync(host, paramedic.UserId);
            Assert.Equal(0, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ActorIdentityUserId == paramedic.UserId)));
        }

        [SkippableFact]
        public async Task OnlyAnAffiliatedParamedicWithEmergenciesOperateOpensEmergencies()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var clinic = await host.Web.CreateClinicAsync();
            var body = Body(Guid.NewGuid(), NowToTheMillisecond());

            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Anonymous().PostAsJsonAsync(Emergencies, body)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Anonymous().GetAsync($"{Emergencies}/{Guid.NewGuid()}")).StatusCode);

            // Clinical staff of the clinic, but without Emergencies.Operate (the Doctor even has Encounters.Create).
            foreach (var (role, type) in new[]
            {
                (SecurityRoles.Doctor, ClinicalStaffType.Physician),
                (SecurityRoles.Receptionist, ClinicalStaffType.OtherClinical),
                (SecurityRoles.Nurse, ClinicalStaffType.Nurse)
            })
            {
                var user = await host.SignInAsync(role, [clinic], staffType: type);
                Assert.Equal(HttpStatusCode.Forbidden, (await user.Client.PostAsJsonAsync(Emergencies, body)).StatusCode);
            }

            // The permission alone is not enough: an active ClinicalStaff with an active affiliation is required.
            var notStaff = await host.SignInAsync(SecurityRoles.Paramedic, [clinic], asClinicalStaff: false);
            await AssertStatusAsync(await notStaff.Client.PostAsJsonAsync(Emergencies, body), HttpStatusCode.Forbidden);
            var unaffiliated = await host.SignInAsync(SecurityRoles.Paramedic, []);
            await AssertStatusAsync(await unaffiliated.Client.PostAsJsonAsync(Emergencies, body), HttpStatusCode.Forbidden);

            Assert.Equal(0, await host.Web.DbAsync(db => db.ClinicalEncounters.CountAsync()));
            Assert.Equal(0, await host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync()));
        }

        [SkippableFact]
        public async Task OnlyTheAssignedParamedicReadsTheEmergency()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var clinic = await host.Web.CreateClinicAsync();
            var assigned = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            // Same base clinic: the base clinic never grants access to a clinic-less emergency.
            var colleague = await host.SignInAsync(SecurityRoles.Paramedic, [clinic]);
            var doctor = await host.SignInAsync(SecurityRoles.Doctor, [clinic], staffType: ClinicalStaffType.Physician);
            var receptionist = await host.SignInAsync(SecurityRoles.Receptionist, [clinic], staffType: ClinicalStaffType.OtherClinical);
            var id = (await (await assigned.Client.PostAsJsonAsync(Emergencies, Body(Guid.NewGuid(), NowToTheMillisecond()))).ReadAsAsync<PrehospitalEmergencySummary>()).Id;

            Assert.Equal(HttpStatusCode.OK, (await assigned.Client.GetAsync($"{Emergencies}/{id}")).StatusCode);
            await AssertStatusAsync(await colleague.Client.GetAsync($"{Emergencies}/{id}"), HttpStatusCode.Forbidden);
            Assert.Equal(HttpStatusCode.Forbidden, (await doctor.Client.GetAsync($"{Emergencies}/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await receptionist.Client.GetAsync($"{Emergencies}/{id}")).StatusCode);
            await AssertStatusAsync(await assigned.Client.GetAsync($"{Emergencies}/{Guid.NewGuid()}"), HttpStatusCode.NotFound);
        }

        [SkippableFact]
        public async Task InvalidRequestsAreRejectedWithoutWritingAnything()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync()]);
            var now = NowToTheMillisecond();

            foreach (var body in new object[]
            {
                Body(Guid.NewGuid(), now.AddMinutes(10)),
                Body(Guid.NewGuid(), now.AddHours(-25)),
                Body(Guid.Empty, now),
                Body(Guid.NewGuid(), now, "   "),
                new { clientRequestId = Guid.NewGuid(), startedAt = now },
                new { clientRequestId = Guid.NewGuid(), chiefComplaint = "Dolor" },
                // Location is out of V1, and nothing the server decides is accepted from the client.
                new { clientRequestId = Guid.NewGuid(), startedAt = now, chiefComplaint = "Dolor", location = new { latitude = 18.47, longitude = -69.9 } },
                new { clientRequestId = Guid.NewGuid(), startedAt = now, chiefComplaint = "Dolor", clinicId = Guid.NewGuid() },
                new { clientRequestId = Guid.NewGuid(), startedAt = now, chiefComplaint = "Dolor", staffId = paramedic.StaffId },
                new { clientRequestId = Guid.NewGuid(), startedAt = now, chiefComplaint = "Dolor", patientId = Guid.NewGuid() },
                new { clientRequestId = Guid.NewGuid(), startedAt = now, chiefComplaint = "Dolor", temporaryPatientCode = "NN-0" }
            })
            {
                Assert.Equal(HttpStatusCode.BadRequest, (await paramedic.Client.PostAsJsonAsync(Emergencies, body)).StatusCode);
            }

            Assert.Equal(0, await host.Web.DbAsync(db => db.ClinicalEncounters.CountAsync()));
            Assert.Equal(0, await host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync()));
        }

        // A hospital encounter is not an emergency of the mobile API, and the clinic-less emergency is not open work of
        // the paramedic's base clinic (lists and hospital reads keep the clinic rule).
        [SkippableFact]
        public async Task AClinicLessEmergencyStaysOutOfTheBaseClinic()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var doctor = await host.Web.SignInAsync(SecurityRoles.Doctor, [baseClinic], staffType: ClinicalStaffType.Physician);
            var id = (await (await paramedic.Client.PostAsJsonAsync(Emergencies, Body(Guid.NewGuid(), NowToTheMillisecond()))).ReadAsAsync<PrehospitalEmergencySummary>()).Id;
            var hospital = await (await doctor.Client.PostAsJsonAsync(WebEncounters, new { type = "Emergency" })).ReadAsAsync<EncounterSummary>();

            var open = await (await doctor.Client.GetAsync(WebEncounters)).ReadAsAsync<List<EncounterSummary>>();
            Assert.Contains(open, e => e.Id == hospital.Id);
            Assert.DoesNotContain(open, e => e.Id == id);
            await AssertStatusAsync(await doctor.Client.GetAsync($"{WebEncounters}/{id}"), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await paramedic.Client.GetAsync($"{Emergencies}/{hospital.Id}"), HttpStatusCode.NotFound);
        }

        // Arrival continuity with an emergency opened through the real mobile path. Requesting and dispatching a transfer
        // of a clinic-less encounter is not exposed yet, so those two steps go through the Domain; the hospital receives it.
        [SkippableFact]
        public async Task AnEmergencyOpenedOnMobileArrivesAtTheHospitalAsTheSameEncounter()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var hospital = await host.Web.CreateClinicAsync("Hospital receptor");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var doctor = await host.Web.SignInAsync(SecurityRoles.Doctor, [hospital], staffType: ClinicalStaffType.Physician);
            var startedAt = NowToTheMillisecond().AddMinutes(-20);
            var opened = await (await paramedic.Client.PostAsJsonAsync(Emergencies, Body(Guid.NewGuid(), startedAt))).ReadAsAsync<PrehospitalEmergencySummary>();

            var transferId = await host.Web.DbAsync(async db =>
            {
                var encounter = await db.ClinicalEncounters.SingleAsync(e => e.Id == opened.Id);
                encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, startedAt.AddMinutes(1));
                encounter.ChangeStatus(ClinicalEncounterStatus.Transferred, startedAt.AddMinutes(5));
                var transfer = ClinicalTransfer.Request(encounter.Id, hospital.Id, startedAt.AddMinutes(2));
                transfer.Dispatch(paramedic.StaffId!.Value, startedAt.AddMinutes(5));
                db.ClinicalTransfers.Add(transfer);
                await db.SaveChangesAsync();
                return transfer.Id;
            });

            Assert.Equal(HttpStatusCode.OK, (await doctor.Client.PostAsync($"/api/web/transfers/{transferId}/receive", null)).StatusCode);

            var arrived = await (await doctor.Client.GetAsync($"{WebEncounters}/{opened.Id}")).ReadAsAsync<EncounterSummary>();
            Assert.Equal((ClinicalEncounterStatus.Received, hospital.Id, opened.TemporaryPatientCode, ClinicalEncounterType.Prehospital),
                (arrived.Status, arrived.ClinicId!.Value, arrived.TemporaryPatientCode, arrived.Type));
            // With a clinic, the clinic rule applies: the paramedic based elsewhere no longer reads it.
            await AssertStatusAsync(await paramedic.Client.GetAsync($"{Emergencies}/{opened.Id}"), HttpStatusCode.Forbidden);
        }

        private static async Task AssertSingleEmergencyAsync(MobileEmergencyHost host, Guid userId)
        {
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalEncounters.CountAsync()));
            Assert.Equal(1, await host.Web.DbAsync(db => db.EncounterStaffAssignments.CountAsync()));
            Assert.Equal(1, await host.Web.DbAsync(db => db.IdempotencyReceipts.CountAsync(r => r.ActorUserId == userId)));
        }

        private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal((int)expected, problem.RootElement.GetProperty("status").GetInt32());
        }
    }
}
