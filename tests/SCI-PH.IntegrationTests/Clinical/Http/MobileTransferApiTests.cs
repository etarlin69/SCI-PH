using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Transport;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Transport;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Transfer and handoff end to end over PostgreSQL: the paramedic requests (Mobile BFF), starts and documents on the way;
    // the destination sees it among its incoming transfers and receives it (Web BFF, unchanged); from then on the mobile
    // writes are rejected and the hospital takes over. Idempotency, transferId scoping and the races of the encounter's
    // and the transfer's concurrency tokens.
    [Collection(PostgresCollection.Name)]
    public sealed class MobileTransferApiTests
    {
        private const string Emergencies = "/api/mobile/emergencies";
        private const string Destinations = "/api/mobile/catalogs/transfer-destinations";
        private readonly PostgresFixture _postgres;

        public MobileTransferApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static DateTimeOffset NowToTheMillisecond()
        {
            var now = DateTimeOffset.UtcNow;
            return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        }

        private static string RequestJson(Guid destination, Guid? key = null, string? reason = "Trauma mayor") =>
            JsonSerializer.Serialize(new { clientRequestId = key ?? Guid.NewGuid(), destinationClinicId = destination, reason });

        private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, string json) =>
            client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

        private static Task<HttpResponseMessage> RequestAsync(StaffSession paramedic, Guid emergencyId, string json) =>
            PostJsonAsync(paramedic.Client, $"{Emergencies}/{emergencyId}/transfers", json);

        private static Task<HttpResponseMessage> StartAsync(StaffSession paramedic, Guid emergencyId, Guid transferId) =>
            paramedic.Client.PostAsync($"{Emergencies}/{emergencyId}/transfers/{transferId}/start", null);

        private static Task<HttpResponseMessage> CancelAsync(StaffSession paramedic, Guid emergencyId, Guid transferId) =>
            paramedic.Client.PostAsync($"{Emergencies}/{emergencyId}/transfers/{transferId}/cancel", null);

        private static async Task<PrehospitalEmergencySummary> OpenAsync(StaffSession paramedic)
        {
            var response = await paramedic.Client.PostAsJsonAsync(Emergencies,
                new { clientRequestId = Guid.NewGuid(), startedAt = NowToTheMillisecond().AddMinutes(-10), chiefComplaint = "Politrauma" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await response.ReadAsAsync<PrehospitalEmergencySummary>();
        }

        private static async Task<EmergencyTransferSummary> RequestedAsync(StaffSession paramedic, Guid emergencyId, Guid destination)
        {
            var response = await RequestAsync(paramedic, emergencyId, RequestJson(destination));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await response.ReadAsAsync<EmergencyTransferSummary>();
        }

        private static async Task<EmergencyTransferSummary> InTransitAsync(StaffSession paramedic, Guid emergencyId, Guid destination)
        {
            var requested = await RequestedAsync(paramedic, emergencyId, destination);
            var response = await StartAsync(paramedic, emergencyId, requested.Id);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadAsAsync<EmergencyTransferSummary>();
        }

        private static Task<(ClinicalEncounterStatus Status, Guid? ClinicId)> EncounterAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == emergencyId).Select(e => new ValueTuple<ClinicalEncounterStatus, Guid?>(e.Status, e.ClinicId)).SingleAsync());

        private static Task<ClinicalTransfer> TransferAsync(MobileEmergencyHost host, Guid transferId) =>
            host.Web.DbAsync(db => db.ClinicalTransfers.AsNoTracking().SingleAsync(t => t.Id == transferId));

        private static Task<int> TransfersAsync(MobileEmergencyHost host, Guid emergencyId) =>
            host.Web.DbAsync(db => db.ClinicalTransfers.CountAsync(t => t.ClinicalEncounterId == emergencyId));

        // The four mobile records, each with a fresh key, for the given emergency.
        private static IEnumerable<(string Url, object Body)> Records(Guid emergencyId, Guid medicationId) =>
        [
            ($"{Emergencies}/{emergencyId}/vital-signs", new { clientRequestId = Guid.NewGuid(), recordedAt = NowToTheMillisecond(), heartRate = 92 }),
            ($"{Emergencies}/{emergencyId}/procedures", new { clientRequestId = Guid.NewGuid(), procedureCode = "OXYGEN-THERAPY", performedAt = NowToTheMillisecond() }),
            ($"{Emergencies}/{emergencyId}/medication-administrations", new { clientRequestId = Guid.NewGuid(), medicationId, dose = 1, doseUnit = "g", route = "Intravenous", administeredAt = NowToTheMillisecond() }),
            ($"{Emergencies}/{emergencyId}/notes", new { clientRequestId = Guid.NewGuid(), occurredAt = NowToTheMillisecond(), content = "Estable durante el traslado." })
        ];

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

        [SkippableFact]
        public async Task TheDestinationsAreTheActiveClinicsBehindTransfersManage()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base Norte");
            var hospital = await host.Web.CreateClinicAsync("Hospital Central");
            var closed = await host.Web.CreateClinicAsync("Cerrado");
            await host.Web.DbAsync(async db => { (await db.Clinics.SingleAsync(c => c.Id == closed.Id)).Deactivate(); await db.SaveChangesAsync(); });
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);

            var response = await paramedic.Client.GetAsync(Destinations);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var items = json.RootElement.EnumerateArray().ToList();
            Assert.Equal(["Base Norte", "Hospital Central"], items.Select(i => i.GetProperty("name").GetString()));
            Assert.All(items, i => Assert.Equal(["id", "name", "type", "municipality", "province"], i.EnumerateObject().Select(p => p.Name)));
            Assert.DoesNotContain(items, i => i.GetProperty("id").GetGuid() == closed.Id);

            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Anonymous().GetAsync(Destinations)).StatusCode);
            var nurse = await host.SignInAsync(SecurityRoles.Nurse, [baseClinic], staffType: ClinicalStaffType.Nurse);
            await AssertProblemAsync(await nurse.Client.GetAsync(Destinations), HttpStatusCode.Forbidden);
            var operateOnly = await WithPermissionsAsync(host, baseClinic, Permissions.Emergencies.Operate);
            await AssertProblemAsync(await operateOnly.Client.GetAsync(Destinations), HttpStatusCode.Forbidden);
        }

        // The whole handoff: request (Created -> InProgress, no origin), the hospital sees it incoming, start (InTransit,
        // Transferred), the four records on the way, the hospital receives it (Received, clinic assigned), the mobile writes
        // are then rejected and the hospital continues the care. With the audit trail of every step.
        [SkippableFact]
        public async Task ThePatientIsTakenDocumentedOnTheWayAndHandedOffToTheHospital()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var hospital = await host.Web.CreateClinicAsync("Hospital receptor");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var doctor = await host.Web.SignInAsync(SecurityRoles.Doctor, [hospital], staffType: ClinicalStaffType.Physician);
            var medication = await host.Web.SeedMedicationAsync();
            var opened = await OpenAsync(paramedic);

            var requested = await RequestedAsync(paramedic, opened.Id, hospital.Id);
            Assert.Equal((ClinicalTransferStatus.Requested, hospital.Id, "Hospital receptor", (DateTimeOffset?)null),
                (requested.Status, requested.Destination.Id, requested.Destination.Name, requested.SentAtUtc));
            Assert.Equal((ClinicalEncounterStatus.InProgress, (Guid?)null), await EncounterAsync(host, opened.Id));
            var stored = await TransferAsync(host, requested.Id);
            Assert.Null(stored.OriginClinicId);
            var emergency = await (await paramedic.Client.GetAsync($"{Emergencies}/{opened.Id}")).ReadAsAsync<PrehospitalEmergencySummary>();
            Assert.Equal(requested, emergency.ActiveTransfer);

            var incoming = await (await doctor.Client.GetAsync("/api/web/transfers/incoming")).ReadAsAsync<List<ClinicalTransferSummary>>();
            var shown = Assert.Single(incoming);
            Assert.Equal((requested.Id, ClinicalTransferStatus.Requested, (ClinicSummary?)null, opened.TemporaryPatientCode),
                (shown.Id, shown.Status, shown.OriginClinic, shown.TemporaryPatientCode));
            await AssertProblemAsync(await doctor.Client.PostAsync($"/api/web/transfers/{requested.Id}/receive", null), HttpStatusCode.Conflict);

            var started = await StartAsync(paramedic, opened.Id, requested.Id);
            Assert.Equal(HttpStatusCode.OK, started.StatusCode);
            var inTransit = await started.ReadAsAsync<EmergencyTransferSummary>();
            Assert.Equal(ClinicalTransferStatus.InTransit, inTransit.Status);
            Assert.Equal((ClinicalEncounterStatus.Transferred, (Guid?)null), await EncounterAsync(host, opened.Id));
            Assert.Equal(paramedic.StaffId, (await TransferAsync(host, requested.Id)).SentByStaffId);

            foreach (var (url, body) in Records(opened.Id, medication.Id))
            {
                Assert.Equal(HttpStatusCode.Created, (await paramedic.Client.PostAsJsonAsync(url, body)).StatusCode);
            }

            // Before the handoff the hospital cannot open the encounter itself.
            await AssertProblemAsync(await doctor.Client.GetAsync($"/api/web/encounters/{opened.Id}"), HttpStatusCode.Forbidden);

            Assert.Equal(HttpStatusCode.OK, (await doctor.Client.PostAsync($"/api/web/transfers/{requested.Id}/receive", null)).StatusCode);

            Assert.Equal((ClinicalEncounterStatus.Received, (Guid?)hospital.Id), await EncounterAsync(host, opened.Id));
            Assert.Equal(ClinicalTransferStatus.Received, (await TransferAsync(host, requested.Id)).Status);
            // The prehospital care ended: the paramedic who took part gets a 409.
            foreach (var (url, body) in Records(opened.Id, medication.Id))
            {
                await AssertProblemAsync(await paramedic.Client.PostAsJsonAsync(url, body), HttpStatusCode.Conflict);
            }

            Assert.Equal(HttpStatusCode.OK, (await doctor.Client.GetAsync($"/api/web/encounters/{opened.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await doctor.Client.PostAsJsonAsync($"/api/web/encounters/{opened.Id}/vital-signs",
                new { type = "HeartRate", value = 84, measuredAtUtc = DateTimeOffset.UtcNow.AddSeconds(-5) })).StatusCode);

            var audit = await host.Web.DbAsync(db => db.ClinicalAuditEntries.AsNoTracking()
                .Where(a => a.ClinicalEncounterId == opened.Id && a.EntityName == nameof(ClinicalTransfer))
                .OrderBy(a => a.OccurredAtUtc).Select(a => new { a.Action, a.ActorStaffId, a.PatientId }).ToListAsync());
            Assert.Equal([AuditActionType.Create, AuditActionType.Transfer, AuditActionType.Transfer], audit.Select(a => a.Action));
            Assert.Equal([paramedic.StaffId, paramedic.StaffId, doctor.StaffId], audit.Select(a => a.ActorStaffId));
            Assert.All(audit, a => Assert.Null(a.PatientId));
            Assert.Equal(4, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == opened.Id
                && (a.EntityName == nameof(VitalSign) || a.EntityName == nameof(ClinicalProcedure) || a.EntityName == "MedicationAdministration" || a.EntityName == nameof(ClinicalNote))
                && a.ActorStaffId == paramedic.StaffId)));
        }

        // The paramedic also affiliated with the receiving clinic: the clinic access does not extend the prehospital care.
        [SkippableFact]
        public async Task AfterTheHandoffAParamedicOfTheReceivingClinicGetsAConflict()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var hospital = await host.Web.CreateClinicAsync("Hospital base");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [hospital]);
            var doctor = await host.Web.SignInAsync(SecurityRoles.Doctor, [hospital], staffType: ClinicalStaffType.Physician);
            var medication = await host.Web.SeedMedicationAsync();
            var opened = await OpenAsync(paramedic);
            var started = await InTransitAsync(paramedic, opened.Id, hospital.Id);
            Assert.Equal(HttpStatusCode.OK, (await doctor.Client.PostAsync($"/api/web/transfers/{started.Id}/receive", null)).StatusCode);

            foreach (var (url, body) in Records(opened.Id, medication.Id))
            {
                await AssertProblemAsync(await paramedic.Client.PostAsJsonAsync(url, body), HttpStatusCode.Conflict);
            }

            await AssertProblemAsync(await CancelAsync(paramedic, opened.Id, started.Id), HttpStatusCode.Conflict);
            await AssertProblemAsync(await StartAsync(paramedic, opened.Id, started.Id), HttpStatusCode.Conflict);
            await AssertProblemAsync(await RequestAsync(paramedic, opened.Id, RequestJson(hospital.Id)), HttpStatusCode.Conflict);
        }

        [SkippableFact]
        public async Task InvalidRequestsAreRejectedWithoutWritingAnything()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var hospital = await host.Web.CreateClinicAsync("Hospital");
            var inactive = await host.Web.CreateClinicAsync("Inactivo");
            await host.Web.DbAsync(async db => { (await db.Clinics.SingleAsync(c => c.Id == inactive.Id)).Deactivate(); await db.SaveChangesAsync(); });
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var opened = await OpenAsync(paramedic);
            string With(string extra) => RequestJson(hospital.Id).TrimEnd('}') + "," + extra + "}";

            foreach (var (json, field) in new[]
            {
                (RequestJson(hospital.Id, reason: new string('r', 501)), "reason"),
                (JsonSerializer.Serialize(new { clientRequestId = Guid.NewGuid(), destinationClinicId = hospital.Id, notes = new string('n', 4001) }), "notes"),
                (RequestJson(Guid.Empty), "destinationClinicId"),
                (RequestJson(hospital.Id, Guid.Empty), "clientRequestId"),
                (With($"\"originClinicId\":\"{baseClinic.Id}\""), (string?)null),
                (With($"\"requestedAtUtc\":\"{DateTimeOffset.UtcNow:O}\""), null),
                (With($"\"requestedByStaffId\":\"{Guid.NewGuid()}\""), null),
                (With($"\"sentAtUtc\":\"{DateTimeOffset.UtcNow:O}\""), null),
                (With($"\"receivedAtUtc\":\"{DateTimeOffset.UtcNow:O}\""), null),
                (With("\"status\":\"InTransit\""), null),
                (With($"\"staffId\":\"{Guid.NewGuid()}\""), null),
                (With($"\"patientId\":\"{Guid.NewGuid()}\""), null),
                (With($"\"clinicId\":\"{Guid.NewGuid()}\""), null),
                (With($"\"ambulanceId\":\"{Guid.NewGuid()}\""), null)
            })
            {
                var response = await RequestAsync(paramedic, opened.Id, json);
                Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{response.StatusCode}: {json[..Math.Min(200, json.Length)]}");
                var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest);
                if (field is not null)
                {
                    Assert.True(problem.TryGetProperty("field", out var actual) && actual.GetString() == field, problem.ToString());
                }
            }

            await AssertProblemAsync(await RequestAsync(paramedic, opened.Id, RequestJson(Guid.NewGuid())), HttpStatusCode.NotFound);
            await AssertProblemAsync(await RequestAsync(paramedic, opened.Id, RequestJson(inactive.Id)), HttpStatusCode.Conflict);
            await AssertProblemAsync(await RequestAsync(paramedic, Guid.NewGuid(), RequestJson(hospital.Id)), HttpStatusCode.NotFound);

            Assert.Equal(0, await TransfersAsync(host, opened.Id));
            Assert.Equal((ClinicalEncounterStatus.Created, (Guid?)null), await EncounterAsync(host, opened.Id));
        }

        // Same key and same request: the same transfer (200). Another payload: 409. Another key while one is active: 409.
        [SkippableFact]
        public async Task RetryingTheRequestReplaysAndOnlyOneTransferIsActive()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var hospital = await host.Web.CreateClinicAsync("Hospital");
            var other = await host.Web.CreateClinicAsync("Otro");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var opened = await OpenAsync(paramedic);
            var key = Guid.NewGuid();

            var first = await RequestAsync(paramedic, opened.Id, RequestJson(hospital.Id, key));
            var replay = await RequestAsync(paramedic, opened.Id, RequestJson(hospital.Id, key, " Trauma mayor "));

            Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK), (first.StatusCode, replay.StatusCode));
            Assert.Equal((await first.ReadAsAsync<EmergencyTransferSummary>()).Id, (await replay.ReadAsAsync<EmergencyTransferSummary>()).Id);
            await AssertProblemAsync(await RequestAsync(paramedic, opened.Id, RequestJson(other.Id, key)), HttpStatusCode.Conflict);
            await AssertProblemAsync(await RequestAsync(paramedic, opened.Id, RequestJson(other.Id)), HttpStatusCode.Conflict);
            Assert.Equal(1, await TransfersAsync(host, opened.Id));
        }

        // Two simultaneous requests: with the same key one creates and the other replays; with different keys the unique
        // index keeps a single active transfer and the other gets a 409.
        [SkippableTheory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ConcurrentRequestsLeaveOneActiveTransfer(bool sameKey)
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var hospital = await host.Web.CreateClinicAsync("Hospital");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);

            for (var round = 0; round < 3; round++)
            {
                var opened = await OpenAsync(paramedic);
                var key = Guid.NewGuid();
                using var start = new ManualResetEventSlim();
                Task<HttpResponseMessage> Submit() => Task.Run(async () =>
                {
                    start.Wait();
                    return await RequestAsync(paramedic, opened.Id, RequestJson(hospital.Id, sameKey ? key : Guid.NewGuid()));
                });
                var tasks = new[] { Submit(), Submit() };
                start.Set();
                var responses = await Task.WhenAll(tasks);

                Assert.Equal(sameKey ? [HttpStatusCode.OK, HttpStatusCode.Created] : [HttpStatusCode.Created, HttpStatusCode.Conflict],
                    responses.Select(r => r.StatusCode).Order());
                Assert.Equal(1, await TransfersAsync(host, opened.Id));
            }
        }

        [SkippableFact]
        public async Task StartAndCancelAreIdempotentByStateAndScopedToTheTransfer()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var hospital = await host.Web.CreateClinicAsync("Hospital");
            var wrongClinic = await host.Web.CreateClinicAsync("Equivocado");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var opened = await OpenAsync(paramedic);
            var otherEmergency = await OpenAsync(paramedic);

            // A wrong destination: cancel (Requested) and request again; a late retry of the first cancel is a replay that
            // does not touch the new transfer.
            var wrong = await RequestedAsync(paramedic, opened.Id, wrongClinic.Id);
            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(paramedic, opened.Id, wrong.Id)).StatusCode);
            Assert.Equal(ClinicalEncounterStatus.InProgress, (await EncounterAsync(host, opened.Id)).Status);
            var right = await RequestedAsync(paramedic, opened.Id, hospital.Id);
            var late = await CancelAsync(paramedic, opened.Id, wrong.Id);
            Assert.Equal(HttpStatusCode.OK, late.StatusCode);
            Assert.Equal(ClinicalTransferStatus.Cancelled, (await late.ReadAsAsync<EmergencyTransferSummary>()).Status);
            Assert.Equal(ClinicalTransferStatus.Requested, (await TransferAsync(host, right.Id)).Status);
            await AssertProblemAsync(await StartAsync(paramedic, opened.Id, wrong.Id), HttpStatusCode.Conflict);

            // Scoped: a transfer of another emergency or an unknown one is not found.
            await AssertProblemAsync(await StartAsync(paramedic, otherEmergency.Id, right.Id), HttpStatusCode.NotFound);
            await AssertProblemAsync(await CancelAsync(paramedic, opened.Id, Guid.NewGuid()), HttpStatusCode.NotFound);

            // Start twice: one transition and one audit entry.
            Assert.Equal(HttpStatusCode.OK, (await StartAsync(paramedic, opened.Id, right.Id)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await StartAsync(paramedic, opened.Id, right.Id)).StatusCode);
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a =>
                a.EntityId == right.Id.ToString() && a.Action == AuditActionType.Transfer)));

            // Cancel while InTransit: the emergency returns to InProgress; a second cancel is a replay.
            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(paramedic, opened.Id, right.Id)).StatusCode);
            Assert.Equal((ClinicalEncounterStatus.InProgress, (Guid?)null), await EncounterAsync(host, opened.Id));
            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(paramedic, opened.Id, right.Id)).StatusCode);
            Assert.Equal(2, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a =>
                a.EntityId == right.Id.ToString() && a.Action == AuditActionType.Transfer)));
            Assert.Null((await (await paramedic.Client.GetAsync($"{Emergencies}/{opened.Id}")).ReadAsAsync<PrehospitalEmergencySummary>()).ActiveTransfer);
        }

        // Permissions are the authority: no token 401; without Emergencies.Operate or Transfers.Manage 403; another
        // paramedic 403; the Web cannot dispatch or cancel a transfer without origin clinic (unchanged).
        [SkippableFact]
        public async Task OnlyTheAssignedParamedicWithBothPermissionsManagesTheTransfer()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var hospital = await host.Web.CreateClinicAsync("Hospital");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var otherParamedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var opened = await OpenAsync(paramedic);

            Assert.Equal(HttpStatusCode.Unauthorized, (await PostJsonAsync(host.Anonymous(), $"{Emergencies}/{opened.Id}/transfers", RequestJson(hospital.Id))).StatusCode);
            var doctor = await host.SignInAsync(SecurityRoles.Doctor, [baseClinic], staffType: ClinicalStaffType.Physician);
            await AssertProblemAsync(await RequestAsync(doctor, opened.Id, RequestJson(hospital.Id)), HttpStatusCode.Forbidden);
            var withoutManage = await WithPermissionsAsync(host, baseClinic, Permissions.Emergencies.Operate, Permissions.Encounters.Read);
            await AssertProblemAsync(await RequestAsync(withoutManage, opened.Id, RequestJson(hospital.Id)), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await RequestAsync(otherParamedic, opened.Id, RequestJson(hospital.Id)), HttpStatusCode.Forbidden);

            var requested = await RequestedAsync(paramedic, opened.Id, hospital.Id);
            await AssertProblemAsync(await StartAsync(otherParamedic, opened.Id, requested.Id), HttpStatusCode.Forbidden);
            var webDoctor = await host.Web.SignInAsync(SecurityRoles.Doctor, [hospital], staffType: ClinicalStaffType.Physician);
            await AssertProblemAsync(await webDoctor.Client.PostAsync($"/api/web/transfers/{requested.Id}/dispatch", null), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await webDoctor.Client.PostAsync($"/api/web/transfers/{requested.Id}/cancel", null), HttpStatusCode.Forbidden);
            Assert.Equal(ClinicalTransferStatus.Requested, (await TransferAsync(host, requested.Id)).Status);
        }

        // Start vs a record: another transaction starts the transport (dispatch + Transferred) while the record runs. The
        // record loses with a 409; the retry with the same key is accepted, Transferred without clinic being the way.
        [SkippableTheory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task ARecordLosingAgainstTheStartIsAcceptedOnRetry(int record)
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var hospital = await host.Web.CreateClinicAsync("Hospital");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var medication = await host.Web.SeedMedicationAsync();
            var opened = await OpenAsync(paramedic);
            var requested = await RequestedAsync(paramedic, opened.Id, hospital.Id);
            var (url, body) = Records(opened.Id, medication.Id).ElementAt(record);

            await using var other = PostgresFixture.Clinical(host.ConnectionString);
            await using var transaction = await other.Database.BeginTransactionAsync();
            var transfer = await other.ClinicalTransfers.SingleAsync(t => t.Id == requested.Id);
            var encounter = await other.ClinicalEncounters.SingleAsync(e => e.Id == opened.Id);
            transfer.Dispatch(paramedic.StaffId!.Value, DateTimeOffset.UtcNow);
            encounter.ChangeStatus(ClinicalEncounterStatus.Transferred, DateTimeOffset.UtcNow);
            await other.SaveChangesAsync();

            var request = paramedic.Client.PostAsJsonAsync(url, body);
            await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, request);
            await transaction.CommitAsync();
            await AssertProblemAsync(await request, HttpStatusCode.Conflict);

            Assert.Equal(HttpStatusCode.Created, (await paramedic.Client.PostAsJsonAsync(url, body)).StatusCode);
            Assert.Equal(ClinicalEncounterStatus.Transferred, (await EncounterAsync(host, opened.Id)).Status);
        }

        // Receive vs a record: the hospital receives while the record runs. The record loses with a 409 and its retry is
        // rejected too (409: the prehospital care ended).
        [SkippableFact]
        public async Task ARecordLosingAgainstTheReceptionIsNotAcceptedOnRetry()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var hospital = await host.Web.CreateClinicAsync("Hospital");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var opened = await OpenAsync(paramedic);
            var started = await InTransitAsync(paramedic, opened.Id, hospital.Id);
            var (url, body) = Records(opened.Id, Guid.Empty).Last();

            var (connection, receive) = await BeginReceiveAsync(host.ConnectionString, started.Id, opened.Id, hospital.Id, paramedic.StaffId!.Value);
            await using (connection)
            {
                var request = paramedic.Client.PostAsJsonAsync(url, body);
                await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, request);
                await receive.CommitAsync();
                await AssertProblemAsync(await request, HttpStatusCode.Conflict);
            }

            await AssertProblemAsync(await paramedic.Client.PostAsJsonAsync(url, body), HttpStatusCode.Conflict);
            Assert.Equal(0, await host.Web.DbAsync(db => db.ClinicalNotes.CountAsync(n => n.ClinicalEncounterId == opened.Id)));
        }

        // Cancel vs receive: the hospital receives while the paramedic cancels; the cancel loses with a 409.
        [SkippableFact]
        public async Task ACancelLosingAgainstTheReceptionIsAConflict()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var hospital = await host.Web.CreateClinicAsync("Hospital");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var opened = await OpenAsync(paramedic);
            var started = await InTransitAsync(paramedic, opened.Id, hospital.Id);

            var (connection, receive) = await BeginReceiveAsync(host.ConnectionString, started.Id, opened.Id, hospital.Id, paramedic.StaffId!.Value);
            await using (connection)
            {
                var request = CancelAsync(paramedic, opened.Id, started.Id);
                await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, request);
                await receive.CommitAsync();
                await AssertProblemAsync(await request, HttpStatusCode.Conflict);
            }

            Assert.Equal(ClinicalTransferStatus.Received, (await TransferAsync(host, started.Id)).Status);
            Assert.Equal((ClinicalEncounterStatus.Received, (Guid?)hospital.Id), await EncounterAsync(host, opened.Id));
        }

        // Identification vs request and vs start: the PatientId concurrency token makes the transfer step lose with a 409,
        // and a retry then succeeds on the identified emergency.
        [SkippableTheory]
        [InlineData("request")]
        [InlineData("start")]
        public async Task ATransferStepLosingAgainstAnIdentificationIsAConflict(string step)
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var hospital = await host.Web.CreateClinicAsync("Hospital");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var opened = await OpenAsync(paramedic);
            var requested = step == "start" ? await RequestedAsync(paramedic, opened.Id, hospital.Id) : null;
            var patient = await host.Web.SeedPatientAsync(nationalId: ClinicalApiHost.RandomNationalId());
            var key = Guid.NewGuid();
            Task<HttpResponseMessage> Step() => step == "start"
                ? StartAsync(paramedic, opened.Id, requested!.Id)
                : RequestAsync(paramedic, opened.Id, RequestJson(hospital.Id, key));

            var (connection, identify) = await BeginIdentifyAsync(host.ConnectionString, opened.Id, patient.Id, paramedic.StaffId!.Value);
            await using (connection)
            {
                var request = Step();
                await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, request);
                await identify.CommitAsync();
                await AssertProblemAsync(await request, HttpStatusCode.Conflict);
            }

            var retry = await Step();
            Assert.True(retry.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created, retry.StatusCode.ToString());
            Assert.Equal(1, await TransfersAsync(host, opened.Id));
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == opened.Id
                && a.EntityName == nameof(ClinicalTransfer) && a.PatientId == patient.Id)));
        }

        // Another writer receives the transfer at the destination (as TransferService.ReceiveAsync) in a transaction that
        // the caller commits.
        private static async Task<(NpgsqlConnection, NpgsqlTransaction)> BeginReceiveAsync(
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
