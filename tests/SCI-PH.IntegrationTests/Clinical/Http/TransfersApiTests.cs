using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
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
using Xunit.Abstractions;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Transfers between clinics through the real Web BFF over PostgreSQL: request and dispatch from the origin, reception at
    // the destination (the same encounter moves there), isolation and the races on the encounter status.
    [Collection(PostgresCollection.Name)]
    public sealed class TransfersApiTests
    {
        private const string Encounters = "/api/web/encounters";
        private const string Transfers = "/api/web/transfers";
        private readonly PostgresFixture _postgres;
        private readonly ITestOutputHelper _output;

        public TransfersApiTests(PostgresFixture postgres, ITestOutputHelper output)
        {
            _postgres = postgres;
            _output = output;
        }

        private sealed record Setup(
            ClinicalApiHost Host, Clinic ClinicA, Clinic ClinicB, StaffSession DoctorA, StaffSession ReceptionA, StaffSession NurseA,
            StaffSession DoctorB, StaffSession ReceptionB, EncounterSummary Encounter);

        private async Task<Setup> InProgressAsync()
        {
            var host = await ClinicalApiHost.StartAsync(_postgres);
            var clinicA = await host.CreateClinicAsync("Hospital origen");
            var clinicB = await host.CreateClinicAsync("Hospital destino");
            var doctorA = await host.SignInAsync(SecurityRoles.Doctor, [clinicA], staffType: ClinicalStaffType.Physician);
            var receptionA = await host.SignInAsync(SecurityRoles.Receptionist, [clinicA]);
            var nurseA = await host.SignInAsync(SecurityRoles.Nurse, [clinicA], staffType: ClinicalStaffType.Nurse);
            var doctorB = await host.SignInAsync(SecurityRoles.Doctor, [clinicB], staffType: ClinicalStaffType.Physician);
            var receptionB = await host.SignInAsync(SecurityRoles.Receptionist, [clinicB]);
            var encounter = await OpenEncounterAsync(host, receptionA, nurseA);
            return new Setup(host, clinicA, clinicB, doctorA, receptionA, nurseA, doctorB, receptionB, encounter);
        }

        private static async Task<EncounterSummary> OpenEncounterAsync(ClinicalApiHost host, StaffSession reception, StaffSession nurse)
        {
            var encounter = await (await reception.Client.PostAsJsonAsync(Encounters,
                new { type = "Emergency", patientId = (await host.SeedPatientAsync()).Id })).ReadAsAsync<EncounterSummary>();
            Assert.Equal(HttpStatusCode.Created, (await nurse.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/vital-signs",
                new { type = "HeartRate", value = 95, measuredAtUtc = DateTimeOffset.UtcNow })).StatusCode);
            return encounter;
        }

        private static async Task<ClinicalTransferSummary> RequestAsync(Setup s, Guid? encounterId = null)
        {
            var response = await s.DoctorA.Client.PostAsJsonAsync($"{Encounters}/{encounterId ?? s.Encounter.Id}/transfers",
                new { destinationClinicId = s.ClinicB.Id, reason = "Requiere UCI (dato de prueba)" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await response.ReadAsAsync<ClinicalTransferSummary>();
        }

        private static async Task<ClinicalTransferSummary> InTransitAsync(Setup s, Guid? encounterId = null)
        {
            var requested = await RequestAsync(s, encounterId);
            var dispatched = await s.DoctorA.Client.PostAsync($"{Transfers}/{requested.Id}/dispatch", null);
            Assert.Equal(HttpStatusCode.OK, dispatched.StatusCode);
            return await dispatched.ReadAsAsync<ClinicalTransferSummary>();
        }

        private static Task<ClinicalEncounter> EncounterAsync(Setup s, Guid id) =>
            s.Host.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == id));

        private static Task<ClinicalTransfer> TransferAsync(Setup s, Guid id) =>
            s.Host.DbAsync(db => db.ClinicalTransfers.AsNoTracking().SingleAsync(t => t.Id == id));

        [SkippableFact]
        public async Task AnEncounterIsRequestedDispatchedAndReceivedAtTheDestinationClinic()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;

            // Request: persisted with origin and destination; the encounter does not move yet.
            var requested = await s.DoctorA.Client.PostAsJsonAsync($"{Encounters}/{id}/transfers",
                new { destinationClinicId = s.ClinicB.Id, reason = "Requiere UCI (dato de prueba)" });
            Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
            var transfer = await requested.ReadAsAsync<ClinicalTransferSummary>();
            Assert.EndsWith($"{Transfers}/{transfer.Id}", requested.Headers.Location!.ToString());
            var stored = await TransferAsync(s, transfer.Id);
            Assert.Equal((s.ClinicA.Id, s.ClinicB.Id, ClinicalTransferStatus.Requested), (stored.OriginClinicId!.Value, stored.DestinationClinicId, stored.Status));
            Assert.Equal(ClinicalEncounterStatus.InProgress, (await EncounterAsync(s, id)).Status);

            // Dispatch: InTransit + Transferred in one transaction; still clinic A while in transit.
            var dispatched = await (await s.DoctorA.Client.PostAsync($"{Transfers}/{transfer.Id}/dispatch", null)).ReadAsAsync<ClinicalTransferSummary>();
            Assert.Equal(ClinicalTransferStatus.InTransit, dispatched.Status);
            Assert.Equal(s.DoctorA.StaffId, dispatched.SentByStaffId);
            var inTransit = await EncounterAsync(s, id);
            Assert.Equal((ClinicalEncounterStatus.Transferred, s.ClinicA.Id), (inTransit.Status, inTransit.ClinicId!.Value));
            Assert.DoesNotContain(await (await s.DoctorA.Client.GetAsync(Encounters)).ReadAsAsync<List<EncounterSummary>>(), e => e.Id == id);

            // Incoming: only the destination sees it; enums as text, identifier masked.
            using (var incoming = JsonDocument.Parse(await s.ReceptionB.Client.GetStringAsync($"{Transfers}/incoming")))
            {
                var item = incoming.RootElement.EnumerateArray().Single();
                Assert.Equal("InTransit", item.GetProperty("status").GetString());
                Assert.Equal("Transferred", item.GetProperty("encounterStatus").GetString());
                Assert.Equal("Hospital origen", item.GetProperty("originClinic").GetProperty("name").GetString());
                Assert.StartsWith("***", item.GetProperty("patient").GetProperty("primaryIdentifier").GetProperty("value").GetString());
            }

            Assert.Empty(await (await s.DoctorA.Client.GetAsync($"{Transfers}/incoming")).ReadAsAsync<List<ClinicalTransferSummary>>());

            // Receive: the same encounter moves to clinic B; the receiver comes from the session.
            var received = await s.DoctorB.Client.PostAsync($"{Transfers}/{transfer.Id}/receive", null);
            Assert.Equal(HttpStatusCode.OK, received.StatusCode);
            var after = await TransferAsync(s, transfer.Id);
            var moved = await EncounterAsync(s, id);
            Assert.Equal((ClinicalTransferStatus.Received, s.DoctorB.StaffId), (after.Status, after.ReceivedByStaffId));
            Assert.Equal((ClinicalEncounterStatus.Received, s.ClinicB.Id, s.Encounter.PatientId), (moved.Status, moved.ClinicId!.Value, moved.PatientId));
            Assert.Equal(2, await s.Host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.ClinicalEncounterId == id && a.Action == AuditActionType.Transfer)));
            Assert.Empty(await (await s.DoctorB.Client.GetAsync($"{Transfers}/incoming")).ReadAsAsync<List<ClinicalTransferSummary>>());

            // Clinic B continues the encounter (history kept); clinic A loses normal clinical access.
            Assert.Contains(await (await s.DoctorB.Client.GetAsync(Encounters)).ReadAsAsync<List<EncounterSummary>>(), e => e.Id == id);
            Assert.Single(await (await s.DoctorB.Client.GetAsync($"{Encounters}/{id}/vital-signs")).ReadAsAsync<List<VitalSignSummary>>());
            Assert.Equal(HttpStatusCode.Created, (await s.DoctorB.Client.PostAsJsonAsync($"{Encounters}/{id}/notes",
                new { type = "Evolution", content = "Recibido estable (dato de prueba).", occurredAtUtc = DateTimeOffset.UtcNow })).StatusCode);
            await AssertStatusAsync(await s.DoctorA.Client.GetAsync($"{Encounters}/{id}"), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await s.DoctorA.Client.GetAsync($"{Encounters}/{id}/notes"), HttpStatusCode.Forbidden);
            // The origin keeps the trace of the transfer.
            Assert.Equal(ClinicalTransferStatus.Received, (await (await s.DoctorA.Client.GetAsync($"{Transfers}/{transfer.Id}")).ReadAsAsync<ClinicalTransferSummary>()).Status);

            await AssertStatusAsync(await s.DoctorB.Client.PostAsync($"{Transfers}/{transfer.Id}/receive", null), HttpStatusCode.Conflict);
            Assert.Equal(HttpStatusCode.NoContent, (await s.DoctorB.Client.PostAsJsonAsync($"{Encounters}/{id}/close", new { })).StatusCode);
        }

        [SkippableFact]
        public async Task PermissionsFollowTheRealRoleMatrix()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var anonymous = s.Host.Anonymous();
            var transfer = await RequestAsync(s);

            foreach (var response in new[]
            {
                await anonymous.GetAsync($"{Transfers}/incoming"),
                await anonymous.GetAsync($"{Transfers}/{transfer.Id}"),
                await anonymous.PostAsJsonAsync($"{Encounters}/{s.Encounter.Id}/transfers", new { destinationClinicId = s.ClinicB.Id }),
                await anonymous.PostAsync($"{Transfers}/{transfer.Id}/dispatch", null),
                await anonymous.PostAsync($"{Transfers}/{transfer.Id}/receive", null)
            })
            {
                await AssertStatusAsync(response, HttpStatusCode.Unauthorized);
            }

            // Nurse: Transfers.Read only.
            Assert.Equal(HttpStatusCode.OK, (await s.NurseA.Client.GetAsync($"{Transfers}/incoming")).StatusCode);
            await AssertStatusAsync(await s.NurseA.Client.PostAsync($"{Transfers}/{transfer.Id}/dispatch", null), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await s.NurseA.Client.PostAsJsonAsync($"{Encounters}/{Guid.NewGuid()}/transfers",
                new { destinationClinicId = s.ClinicB.Id }), HttpStatusCode.Forbidden);
            // Receptionist: Transfers.Manage (real matrix) — dispatches from the origin and receives at the destination.
            Assert.Equal(HttpStatusCode.OK, (await s.ReceptionA.Client.PostAsync($"{Transfers}/{transfer.Id}/dispatch", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await s.ReceptionB.Client.PostAsync($"{Transfers}/{transfer.Id}/receive", null)).StatusCode);
            Assert.Equal(s.ReceptionB.StaffId, (await TransferAsync(s, transfer.Id)).ReceivedByStaffId);
            // Being clinical staff is required as well as the permission.
            var unlinked = await s.Host.SignInAsync(SecurityRoles.Doctor, [], asClinicalStaff: false);
            await AssertStatusAsync(await unlinked.Client.GetAsync($"{Transfers}/incoming"), HttpStatusCode.Forbidden);
        }

        [SkippableFact]
        public async Task NotFoundValidationConflictsAndClinicIsolation()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;
            var request = $"{Encounters}/{id}/transfers";
            var inactive = await s.Host.CreateClinicAsync("Hospital inactivo");
            await s.Host.DbAsync(async db =>
            {
                (await db.Clinics.SingleAsync(c => c.Id == inactive.Id)).Deactivate();
                await db.SaveChangesAsync();
            });

            await AssertStatusAsync(await s.DoctorA.Client.PostAsJsonAsync($"{Encounters}/{Guid.NewGuid()}/transfers", new { destinationClinicId = s.ClinicB.Id }), HttpStatusCode.NotFound);
            await AssertStatusAsync(await s.DoctorA.Client.PostAsJsonAsync(request, new { destinationClinicId = Guid.NewGuid() }), HttpStatusCode.NotFound);
            await AssertStatusAsync(await s.DoctorA.Client.PostAsync($"{Transfers}/{Guid.NewGuid()}/receive", null), HttpStatusCode.NotFound);
            await AssertStatusAsync(await s.DoctorA.Client.PostAsJsonAsync(request, new { destinationClinicId = inactive.Id }), HttpStatusCode.Conflict);
            await AssertStatusAsync(await s.DoctorA.Client.PostAsJsonAsync(request, new { destinationClinicId = s.ClinicA.Id }), HttpStatusCode.BadRequest);
            // The origin is never taken from the client.
            await AssertStatusAsync(await s.DoctorA.Client.PostAsJsonAsync(request, new { destinationClinicId = s.ClinicB.Id, originClinicId = s.ClinicB.Id }), HttpStatusCode.BadRequest);

            var transfer = await RequestAsync(s);
            await AssertStatusAsync(await s.DoctorA.Client.PostAsJsonAsync(request, new { destinationClinicId = s.ClinicB.Id }), HttpStatusCode.Conflict);
            // A Requested transfer cannot be received; only the origin dispatches it.
            await AssertStatusAsync(await s.DoctorB.Client.PostAsync($"{Transfers}/{transfer.Id}/receive", null), HttpStatusCode.Conflict);
            await AssertStatusAsync(await s.DoctorB.Client.PostAsync($"{Transfers}/{transfer.Id}/dispatch", null), HttpStatusCode.Forbidden);
            Assert.Equal(HttpStatusCode.OK, (await s.DoctorA.Client.PostAsync($"{Transfers}/{transfer.Id}/dispatch", null)).StatusCode);
            await AssertStatusAsync(await s.DoctorA.Client.PostAsync($"{Transfers}/{transfer.Id}/dispatch", null), HttpStatusCode.Conflict);
            // A transferred encounter cannot be closed or written by the origin.
            await AssertStatusAsync(await s.DoctorA.Client.PostAsJsonAsync($"{Encounters}/{id}/close", new { }), HttpStatusCode.Conflict);

            // Only the destination receives; a third clinic sees nothing of it.
            var clinicC = await s.Host.CreateClinicAsync("Hospital ajeno");
            var doctorC = await s.Host.SignInAsync(SecurityRoles.Doctor, [clinicC], staffType: ClinicalStaffType.Physician);
            await AssertStatusAsync(await s.DoctorA.Client.PostAsync($"{Transfers}/{transfer.Id}/receive", null), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await doctorC.Client.PostAsync($"{Transfers}/{transfer.Id}/receive", null), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await doctorC.Client.GetAsync($"{Transfers}/{transfer.Id}"), HttpStatusCode.Forbidden);
            Assert.Empty(await (await doctorC.Client.GetAsync($"{Transfers}/incoming")).ReadAsAsync<List<ClinicalTransferSummary>>());
            await AssertStatusAsync(await s.DoctorB.Client.GetAsync($"{Transfers}/incoming?clinicId={clinicC.Id}"), HttpStatusCode.Forbidden);
            Assert.Equal(ClinicalTransferStatus.InTransit, (await TransferAsync(s, transfer.Id)).Status);
        }

        [SkippableFact]
        public async Task APrehospitalEncounterArrivesAtTheHospitalAsTheSameEncounter()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            // Prehospital care (no clinic) already on its way: built through the Domain, as the paramedic flow will.
            var startedAt = DateTimeOffset.UtcNow.AddMinutes(-20);
            var encounter = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Prehospital, startedAt);
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, startedAt);
            encounter.ChangeStatus(ClinicalEncounterStatus.Transferred, startedAt.AddMinutes(5));
            var transfer = ClinicalTransfer.Request(encounter.Id, s.ClinicB.Id, startedAt.AddMinutes(2));
            transfer.Dispatch(s.DoctorA.StaffId!.Value, startedAt.AddMinutes(5));
            await s.Host.DbAsync(async db =>
            {
                db.ClinicalEncounters.Add(encounter);
                db.ClinicalTransfers.Add(transfer);
                await db.SaveChangesAsync();
            });

            var incoming = Assert.Single(await (await s.DoctorB.Client.GetAsync($"{Transfers}/incoming")).ReadAsAsync<List<ClinicalTransferSummary>>());
            Assert.Null(incoming.OriginClinic);
            Assert.Equal(HttpStatusCode.OK, (await s.DoctorB.Client.PostAsync($"{Transfers}/{transfer.Id}/receive", null)).StatusCode);

            var arrived = await EncounterAsync(s, encounter.Id);
            Assert.Equal((ClinicalEncounterStatus.Received, s.ClinicB.Id), (arrived.Status, arrived.ClinicId!.Value));
            Assert.Equal(encounter.TemporaryPatientCode, arrived.TemporaryPatientCode);
            Assert.Equal(HttpStatusCode.OK, (await s.DoctorB.Client.GetAsync($"{Encounters}/{encounter.Id}")).StatusCode);
        }

        // Two receptions of the same transfer at the same time: one wins, the other gets a 409 and changes nothing.
        [SkippableFact]
        public async Task ConcurrentReceptionsYieldOneReceiver()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;

            for (var round = 0; round < 3; round++)
            {
                var encounter = round == 0 ? s.Encounter : await OpenEncounterAsync(s.Host, s.ReceptionA, s.NurseA);
                var transfer = await InTransitAsync(s, encounter.Id);
                using var start = new ManualResetEventSlim();
                Task<HttpResponseMessage> Run(StaffSession who) => Task.Run(async () => { start.Wait(); return await who.Client.PostAsync($"{Transfers}/{transfer.Id}/receive", null); });
                var tasks = new[] { Run(s.DoctorB), Run(s.ReceptionB) };
                start.Set();
                var responses = await Task.WhenAll(tasks);

                Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
                await AssertStatusAsync(responses.Single(r => r.StatusCode != HttpStatusCode.OK), HttpStatusCode.Conflict);
                var stored = await TransferAsync(s, transfer.Id);
                Assert.Contains(stored.ReceivedByStaffId, new[] { s.DoctorB.StaffId, s.ReceptionB.StaffId });
                // Dispatch + exactly one reception.
                Assert.Equal(2, await s.Host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(
                    a => a.EntityId == transfer.Id.ToString() && a.Action == AuditActionType.Transfer)));
            }
        }

        [SkippableFact]
        public async Task ConcurrentDispatchesOfTheSameTransferYieldOneDispatch()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var transfer = await RequestAsync(s);

            using var start = new ManualResetEventSlim();
            Task<HttpResponseMessage> Run(StaffSession who) => Task.Run(async () => { start.Wait(); return await who.Client.PostAsync($"{Transfers}/{transfer.Id}/dispatch", null); });
            var tasks = new[] { Run(s.DoctorA), Run(s.ReceptionA) };
            start.Set();
            var responses = await Task.WhenAll(tasks);

            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            await AssertStatusAsync(responses.Single(r => r.StatusCode != HttpStatusCode.OK), HttpStatusCode.Conflict);
            Assert.Equal(ClinicalTransferStatus.InTransit, (await TransferAsync(s, transfer.Id)).Status);
        }

        // Another transaction closes the encounter while the dispatch is processed: the dispatch blocks on the encounter row
        // and, once the close commits, fails with 409; the transfer stays Requested.
        [SkippableFact]
        public async Task AConcurrentCloseWinsOverADispatch()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var transfer = await RequestAsync(s);

            var (connection, close) = await ClinicalRace.BeginCloseAsync(s.Host.ConnectionString, s.Encounter.Id);
            await using var __ = connection;
            await using var ___ = close;
            var dispatch = s.DoctorA.Client.PostAsync($"{Transfers}/{transfer.Id}/dispatch", null);
            await ClinicalRace.WaitUntilBlockedOnLockAsync(s.Host.ConnectionString, dispatch);
            await close.CommitAsync();

            await AssertStatusAsync(await dispatch, HttpStatusCode.Conflict);
            Assert.Equal(ClinicalTransferStatus.Requested, (await TransferAsync(s, transfer.Id)).Status);
            Assert.Equal(ClinicalEncounterStatus.Closed, (await EncounterAsync(s, s.Encounter.Id)).Status);
        }

        // A close and a request race: the request re-checks the encounter tokens, so it is not stored after the close.
        [SkippableFact]
        public async Task AConcurrentCloseWinsOverARequest()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;

            var (connection, close) = await ClinicalRace.BeginCloseAsync(s.Host.ConnectionString, s.Encounter.Id);
            await using var __ = connection;
            await using var ___ = close;
            var request = s.DoctorA.Client.PostAsJsonAsync($"{Encounters}/{s.Encounter.Id}/transfers", new { destinationClinicId = s.ClinicB.Id });
            await ClinicalRace.WaitUntilBlockedOnLockAsync(s.Host.ConnectionString, request);
            await close.CommitAsync();

            await AssertStatusAsync(await request, HttpStatusCode.Conflict);
            Assert.False(await s.Host.DbAsync(db => db.ClinicalTransfers.AnyAsync(t => t.ClinicalEncounterId == s.Encounter.Id)));
        }

        // Another writer changes the transferred encounter (e.g. the transfer is cancelled, Transferred -> InProgress) while
        // the reception is processed: the reception loses with a 409 and the encounter stays at the origin clinic.
        [SkippableFact]
        public async Task AConcurrentEncounterChangeWinsOverAReception()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var transfer = await InTransitAsync(s);

            var (connection, change) = await ClinicalRace.BeginStatusChangeAsync(s.Host.ConnectionString, s.Encounter.Id,
                (int)ClinicalEncounterStatus.Transferred, (int)ClinicalEncounterStatus.InProgress);
            await using var __ = connection;
            await using var ___ = change;
            var receive = s.DoctorB.Client.PostAsync($"{Transfers}/{transfer.Id}/receive", null);
            await ClinicalRace.WaitUntilBlockedOnLockAsync(s.Host.ConnectionString, receive);
            await change.CommitAsync();

            await AssertStatusAsync(await receive, HttpStatusCode.Conflict);
            var encounter = await EncounterAsync(s, s.Encounter.Id);
            Assert.Equal(s.ClinicA.Id, encounter.ClinicId);
            Assert.Equal(ClinicalTransferStatus.InTransit, (await TransferAsync(s, transfer.Id)).Status);
        }

        private static Task<int> ActiveTransfersAsync(Setup s, Guid encounterId) =>
            s.Host.DbAsync(db => db.ClinicalTransfers.CountAsync(t =>
                t.ClinicalEncounterId == encounterId && ClinicalTransfer.ActiveStatuses.Contains(t.Status)));

        private static async Task<HttpResponseMessage[]> ConcurrentlyAsync(params Func<Task<HttpResponseMessage>>[] calls)
        {
            using var start = new ManualResetEventSlim();
            var tasks = calls.Select(call => Task.Run(async () => { start.Wait(); return await call(); })).ToArray();
            start.Set();
            return await Task.WhenAll(tasks);
        }

        // Two real concurrent requests for the same encounter: one transfer is stored, the other request gets a 409
        // (from the Application check or, when both passed it, from the filtered unique index). Never a 500.
        [SkippableFact]
        public async Task ConcurrentRequestTransfersOnlyOneWins()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;

            for (var round = 0; round < 4; round++)
            {
                var encounter = round == 0 ? s.Encounter : await OpenEncounterAsync(s.Host, s.ReceptionA, s.NurseA);
                var body = new { destinationClinicId = s.ClinicB.Id };
                var responses = await ConcurrentlyAsync(
                    () => s.DoctorA.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/transfers", body),
                    () => s.ReceptionA.Client.PostAsJsonAsync($"{Encounters}/{encounter.Id}/transfers", body));

                Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
                await AssertStatusAsync(responses.Single(r => r.StatusCode != HttpStatusCode.Created), HttpStatusCode.Conflict);
                Assert.Equal(1, await ActiveTransfersAsync(s, encounter.Id));
            }
        }

        // Requested and InTransit block another request; Cancelled, Received and Completed do not.
        [SkippableFact]
        public async Task OnlyActiveTransfersBlockANewRequest()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;
            var toB = new { destinationClinicId = s.ClinicB.Id };

            var first = await RequestAsync(s);
            await AssertStatusAsync(await s.DoctorA.Client.PostAsJsonAsync($"{Encounters}/{id}/transfers", toB), HttpStatusCode.Conflict);
            Assert.Equal(HttpStatusCode.OK, (await s.DoctorA.Client.PostAsync($"{Transfers}/{first.Id}/dispatch", null)).StatusCode);
            await AssertStatusAsync(await s.DoctorA.Client.PostAsJsonAsync($"{Encounters}/{id}/transfers", toB), HttpStatusCode.Conflict);
            Assert.Equal(1, await ActiveTransfersAsync(s, id));

            // Cancelled: the encounter is back in progress at the origin and can be transferred again.
            Assert.Equal(HttpStatusCode.OK, (await s.DoctorA.Client.PostAsync($"{Transfers}/{first.Id}/cancel", null)).StatusCode);
            var second = await InTransitAsync(s);
            Assert.Equal(HttpStatusCode.OK, (await s.DoctorB.Client.PostAsync($"{Transfers}/{second.Id}/receive", null)).StatusCode);

            // Received: once the encounter is again in a transferable status (Received -> InProgress through the Domain;
            // no endpoint exposes it yet), clinic B can request another transfer.
            await MoveEncounterAsync(s, id, e => e.ChangeStatus(ClinicalEncounterStatus.InProgress, DateTimeOffset.UtcNow));
            var backToA = await s.DoctorB.Client.PostAsJsonAsync($"{Encounters}/{id}/transfers", new { destinationClinicId = s.ClinicA.Id });
            Assert.Equal(HttpStatusCode.Created, backToA.StatusCode);
            var third = await backToA.ReadAsAsync<ClinicalTransferSummary>();
            Assert.Equal(s.ClinicB.Id, third.OriginClinic!.Id);

            // Completed (built through the Domain on the received transfer) does not block either.
            Assert.Equal(HttpStatusCode.OK, (await s.DoctorB.Client.PostAsync($"{Transfers}/{third.Id}/cancel", null)).StatusCode);
            await s.Host.DbAsync(async db =>
            {
                (await db.ClinicalTransfers.SingleAsync(t => t.Id == second.Id)).Complete(DateTimeOffset.UtcNow);
                await db.SaveChangesAsync();
            });
            Assert.Equal(HttpStatusCode.Created, (await s.DoctorB.Client.PostAsJsonAsync($"{Encounters}/{id}/transfers",
                new { destinationClinicId = s.ClinicA.Id })).StatusCode);
            var statuses = await s.Host.DbAsync(db => db.ClinicalTransfers.Where(t => t.ClinicalEncounterId == id).Select(t => t.Status).ToListAsync());
            Assert.Equal(
                [ClinicalTransferStatus.Requested, ClinicalTransferStatus.Completed, ClinicalTransferStatus.Cancelled, ClinicalTransferStatus.Cancelled],
                statuses.OrderBy(x => x));
        }

        [SkippableFact]
        public async Task CancellationFromTheOriginRestoresTheEncounterAndIsAudited()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var id = s.Encounter.Id;

            // Requested: only the transfer changes.
            var requested = await RequestAsync(s);
            var cancelled = await (await s.ReceptionA.Client.PostAsync($"{Transfers}/{requested.Id}/cancel", null)).ReadAsAsync<ClinicalTransferSummary>();
            Assert.Equal(ClinicalTransferStatus.Cancelled, cancelled.Status);
            Assert.NotNull((await TransferAsync(s, requested.Id)).CancelledAtUtc);
            Assert.Equal(ClinicalEncounterStatus.InProgress, (await EncounterAsync(s, id)).Status);

            // InTransit: the encounter returns Transferred -> InProgress, still at the origin, and leaves clinic B's inbox.
            var inTransit = await InTransitAsync(s);
            Assert.Single(await (await s.DoctorB.Client.GetAsync($"{Transfers}/incoming")).ReadAsAsync<List<ClinicalTransferSummary>>());
            Assert.Equal(HttpStatusCode.OK, (await s.DoctorA.Client.PostAsync($"{Transfers}/{inTransit.Id}/cancel", null)).StatusCode);
            var back = await EncounterAsync(s, id);
            Assert.Equal((ClinicalEncounterStatus.InProgress, s.ClinicA.Id), (back.Status, back.ClinicId!.Value));
            Assert.Empty(await (await s.DoctorB.Client.GetAsync($"{Transfers}/incoming")).ReadAsAsync<List<ClinicalTransferSummary>>());
            Assert.Contains(await (await s.DoctorA.Client.GetAsync(Encounters)).ReadAsAsync<List<EncounterSummary>>(), e => e.Id == id);
            await AssertStatusAsync(await s.DoctorB.Client.PostAsync($"{Transfers}/{inTransit.Id}/receive", null), HttpStatusCode.Conflict);
            // Request, cancel, request, dispatch, cancel: two cancellations audited as Transfer on their transfers.
            Assert.Equal(1, await s.Host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.EntityId == requested.Id.ToString() && a.Action == AuditActionType.Transfer)));
            Assert.Equal(2, await s.Host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.EntityId == inTransit.Id.ToString() && a.Action == AuditActionType.Transfer)));
        }

        [SkippableFact]
        public async Task OnlyTheOriginCancelsAnActiveTransfer()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var transfer = await InTransitAsync(s);
            var clinicC = await s.Host.CreateClinicAsync("Hospital ajeno");
            var doctorC = await s.Host.SignInAsync(SecurityRoles.Doctor, [clinicC], staffType: ClinicalStaffType.Physician);
            var cancel = $"{Transfers}/{transfer.Id}/cancel";

            await AssertStatusAsync(await s.Host.Anonymous().PostAsync(cancel, null), HttpStatusCode.Unauthorized);
            await AssertStatusAsync(await s.NurseA.Client.PostAsync(cancel, null), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await s.DoctorB.Client.PostAsync(cancel, null), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await doctorC.Client.PostAsync(cancel, null), HttpStatusCode.Forbidden);
            await AssertStatusAsync(await s.DoctorA.Client.PostAsync($"{Transfers}/{Guid.NewGuid()}/cancel", null), HttpStatusCode.NotFound);
            Assert.Equal(ClinicalTransferStatus.InTransit, (await TransferAsync(s, transfer.Id)).Status);

            Assert.Equal(HttpStatusCode.OK, (await s.DoctorB.Client.PostAsync($"{Transfers}/{transfer.Id}/receive", null)).StatusCode);
            await AssertStatusAsync(await s.DoctorA.Client.PostAsync(cancel, null), HttpStatusCode.Conflict);

            var requested = await RequestAsync(s, (await OpenEncounterAsync(s.Host, s.ReceptionA, s.NurseA)).Id);
            Assert.Equal(HttpStatusCode.OK, (await s.DoctorA.Client.PostAsync($"{Transfers}/{requested.Id}/cancel", null)).StatusCode);
            await AssertStatusAsync(await s.DoctorA.Client.PostAsync($"{Transfers}/{requested.Id}/cancel", null), HttpStatusCode.Conflict);
            await AssertStatusAsync(await s.DoctorA.Client.PostAsync($"{Transfers}/{requested.Id}/dispatch", null), HttpStatusCode.Conflict);
        }

        [SkippableFact]
        public async Task ConcurrentCancellationsYieldOneCancellation()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;

            for (var round = 0; round < 3; round++)
            {
                var encounter = round == 0 ? s.Encounter : await OpenEncounterAsync(s.Host, s.ReceptionA, s.NurseA);
                var transfer = round % 2 == 0 ? await RequestAsync(s, encounter.Id) : await InTransitAsync(s, encounter.Id);
                var responses = await ConcurrentlyAsync(
                    () => s.DoctorA.Client.PostAsync($"{Transfers}/{transfer.Id}/cancel", null),
                    () => s.ReceptionA.Client.PostAsync($"{Transfers}/{transfer.Id}/cancel", null));

                Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
                await AssertStatusAsync(responses.Single(r => r.StatusCode != HttpStatusCode.OK), HttpStatusCode.Conflict);
                Assert.Equal(1, await s.Host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(
                    a => a.EntityId == transfer.Id.ToString() && a.Action == AuditActionType.Transfer)) - (round % 2));
                Assert.Equal(ClinicalEncounterStatus.InProgress, (await EncounterAsync(s, encounter.Id)).Status);
            }
        }

        // Cancel and dispatch of the same Requested transfer. They never interleave: either the cancel wins (the dispatch gets
        // a 409, encounter in progress), or the dispatch commits first and then the cancel legitimately cancels the
        // InTransit transfer (both 200, encounter back in progress), or the dispatch wins and the cancel gets a 409
        // (InTransit, encounter transferred). Never a cancelled transfer with a transferred encounter, never a 500.
        [SkippableFact]
        public async Task ACancelAndADispatchOfTheSameTransferNeverInterleave()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;

            for (var round = 0; round < 4; round++)
            {
                var encounter = round == 0 ? s.Encounter : await OpenEncounterAsync(s.Host, s.ReceptionA, s.NurseA);
                var transfer = await RequestAsync(s, encounter.Id);
                var responses = await ConcurrentlyAsync(
                    () => s.DoctorA.Client.PostAsync($"{Transfers}/{transfer.Id}/cancel", null),
                    () => s.ReceptionA.Client.PostAsync($"{Transfers}/{transfer.Id}/dispatch", null));

                Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
                Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
                var cancel = responses[0].StatusCode;
                var (stored, state) = (await TransferAsync(s, transfer.Id), await EncounterAsync(s, encounter.Id));
                var expected = cancel == HttpStatusCode.OK
                    ? (ClinicalTransferStatus.Cancelled, ClinicalEncounterStatus.InProgress)
                    : (ClinicalTransferStatus.InTransit, ClinicalEncounterStatus.Transferred);
                Assert.Equal(expected, (stored.Status, state.Status));
                // Both applied only in sequence: the dispatch audited before the cancel.
                var audits = await s.Host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.EntityId == transfer.Id.ToString() && a.Action == AuditActionType.Transfer));
                Assert.Equal(responses.Count(r => r.StatusCode == HttpStatusCode.OK), audits);
            }
        }

        // Cancel at the origin and reception at the destination of the same InTransit transfer: exactly one applies and the
        // other changes nothing (Status is a concurrency token of both the transfer and the encounter).
        // - Cancel wins: the reception can only lose on the state (409: the transfer is cancelled, the encounter is back in
        //   progress, or the concurrency token changed). Final: Cancelled, encounter InProgress at the origin clinic.
        // - Reception wins: the cancel loses with 409 when it reads the transfer after the reception (Received transfers
        //   cannot be cancelled, or the concurrency token changed), or with 403 when it reads the transfer before and the
        //   encounter after: the encounter already belongs to the destination clinic, where the origin doctor is not
        //   affiliated (EnsureClinic). Final: Received, encounter Received at the destination clinic.
        [SkippableFact]
        public async Task ACancelAndAReceptionOfTheSameTransferNeverBothApply()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;

            for (var round = 0; round < 4; round++)
            {
                var encounter = round == 0 ? s.Encounter : await OpenEncounterAsync(s.Host, s.ReceptionA, s.NurseA);
                var transfer = await InTransitAsync(s, encounter.Id);
                var responses = await ConcurrentlyAsync(
                    () => s.DoctorA.Client.PostAsync($"{Transfers}/{transfer.Id}/cancel", null),
                    () => s.DoctorB.Client.PostAsync($"{Transfers}/{transfer.Id}/receive", null));
                var (cancel, receive) = (responses[0], responses[1]);

                Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
                var (stored, state) = (await TransferAsync(s, transfer.Id), await EncounterAsync(s, encounter.Id));
                if (cancel.StatusCode == HttpStatusCode.OK)
                {
                    await AssertStatusAsync(receive, HttpStatusCode.Conflict);
                    Assert.Equal(
                        (ClinicalTransferStatus.Cancelled, ClinicalEncounterStatus.InProgress, (Guid?)s.ClinicA.Id),
                        (stored.Status, state.Status, state.ClinicId));
                }
                else
                {
                    Assert.Equal(HttpStatusCode.OK, receive.StatusCode);
                    await AssertStatusAsync(cancel, HttpStatusCode.Conflict, HttpStatusCode.Forbidden);
                    Assert.Equal(
                        (ClinicalTransferStatus.Received, ClinicalEncounterStatus.Received, (Guid?)s.ClinicB.Id),
                        (stored.Status, state.Status, state.ClinicId));
                }

                // The dispatch and the single winner: the loser left no audit entry.
                var audits = await s.Host.DbAsync(db => db.ClinicalAuditEntries.CountAsync(a => a.EntityId == transfer.Id.ToString() && a.Action == AuditActionType.Transfer));
                Assert.Equal(2, audits);
                _output.WriteLine($"round {round}: {(cancel.StatusCode == HttpStatusCode.OK ? "cancel" : "receive")} won, loser {(int)(cancel.StatusCode == HttpStatusCode.OK ? receive : cancel).StatusCode}");
            }
        }

        // Cancelling a Requested transfer does not touch the encounter, so it does not conflict with closing it: both apply.
        [SkippableFact]
        public async Task CancellingARequestedTransferAndClosingTheEncounterBothApply()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            var s = await InProgressAsync();
            await using var _ = s.Host;
            var transfer = await RequestAsync(s);

            var responses = await ConcurrentlyAsync(
                () => s.ReceptionA.Client.PostAsync($"{Transfers}/{transfer.Id}/cancel", null),
                () => s.DoctorA.Client.PostAsJsonAsync($"{Encounters}/{s.Encounter.Id}/close", new { }));

            Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, responses[1].StatusCode);
            Assert.Equal(ClinicalTransferStatus.Cancelled, (await TransferAsync(s, transfer.Id)).Status);
            Assert.Equal(ClinicalEncounterStatus.Closed, (await EncounterAsync(s, s.Encounter.Id)).Status);
        }

        private static Task MoveEncounterAsync(Setup s, Guid encounterId, Action<ClinicalEncounter> domainCall) =>
            s.Host.DbAsync(async db =>
            {
                domainCall(await db.ClinicalEncounters.SingleAsync(e => e.Id == encounterId));
                await db.SaveChangesAsync();
            });

        private static async Task AssertStatusAsync(HttpResponseMessage response, params HttpStatusCode[] allowed)
        {
            Assert.Contains(response.StatusCode, allowed);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal((int)response.StatusCode, problem.RootElement.GetProperty("status").GetInt32());
        }
    }
}
