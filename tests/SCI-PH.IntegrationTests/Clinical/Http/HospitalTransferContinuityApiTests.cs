using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Transport;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // Hospital continuity end to end over PostgreSQL: the paramedic takes the NN patient to hospital A (Mobile BFF); A
    // receives it, starts care with its first record (Received -> InProgress) and sends the patient on to hospital B
    // (Web BFF); B receives the same encounter, starts care and discharges it. Plus the two reads of the outgoing-transfer
    // workspace: destinations and the encounter's transfers.
    [Collection(PostgresCollection.Name)]
    public sealed class HospitalTransferContinuityApiTests
    {
        private const string Emergencies = "/api/mobile/emergencies";
        private const string Encounters = "/api/web/encounters";
        private const string Transfers = "/api/web/transfers";
        private readonly PostgresFixture _postgres;

        public HospitalTransferContinuityApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static DateTimeOffset NowToTheMillisecond()
        {
            var now = DateTimeOffset.UtcNow;
            return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        }

        private static Task<ClinicalEncounterStatus> StatusAsync(ClinicalApiHost host, Guid encounterId) =>
            host.DbAsync(db => db.ClinicalEncounters.Where(e => e.Id == encounterId).Select(e => e.Status).SingleAsync());

        private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return problem.RootElement.Clone();
        }

        // Paramedic -> hospital A, received there: the encounter is Received at A with nothing recorded by A yet.
        private static async Task<(PrehospitalEmergencySummary Emergency, Guid PrehospitalTransferId)> ArrivedAtAsync(
            MobileEmergencyHost host, Clinic hospital, StaffSession receiver)
        {
            var baseClinic = await host.Web.CreateClinicAsync("Base ambulancias");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var opened = await paramedic.Client.PostAsJsonAsync(Emergencies,
                new { clientRequestId = Guid.NewGuid(), startedAt = NowToTheMillisecond().AddMinutes(-20), chiefComplaint = "Politrauma" });
            Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
            var emergency = await opened.ReadAsAsync<PrehospitalEmergencySummary>();
            Assert.Equal(HttpStatusCode.Created, (await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{emergency.Id}/vital-signs",
                new { clientRequestId = Guid.NewGuid(), recordedAt = NowToTheMillisecond(), heartRate = 110 })).StatusCode);
            var requested = await (await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{emergency.Id}/transfers",
                new { clientRequestId = Guid.NewGuid(), destinationClinicId = hospital.Id, reason = "Trauma mayor" })).ReadAsAsync<EmergencyTransferSummary>();
            Assert.Equal(HttpStatusCode.OK, (await paramedic.Client.PostAsync($"{Emergencies}/{emergency.Id}/transfers/{requested.Id}/start", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await receiver.Client.PostAsync($"{Transfers}/{requested.Id}/receive", null)).StatusCode);
            return (emergency, requested.Id);
        }

        [SkippableFact]
        public async Task TheSameEncounterGoesFromTheParamedicThroughHospitalAToHospitalBAndIsDischarged()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var hospitalA = await host.Web.CreateClinicAsync("Hospital A");
            var hospitalB = await host.Web.CreateClinicAsync("Hospital Receptor Demo");
            var doctorA = await host.Web.SignInAsync(SecurityRoles.Doctor, [hospitalA], staffType: ClinicalStaffType.Physician);
            var nurseA = await host.Web.SignInAsync(SecurityRoles.Nurse, [hospitalA], staffType: ClinicalStaffType.Nurse);
            var doctorB = await host.Web.SignInAsync(SecurityRoles.Doctor, [hospitalB], staffType: ClinicalStaffType.Physician);
            var (emergency, prehospitalTransfer) = await ArrivedAtAsync(host, hospitalA, doctorA);
            var id = emergency.Id;
            Assert.NotNull(emergency.TemporaryPatientCode);
            Assert.Equal(ClinicalEncounterStatus.Received, await StatusAsync(host.Web, id));

            // Before care starts at A, the patient cannot be sent on (the 409 of the visual QA), and nothing is written.
            await AssertProblemAsync(await doctorA.Client.PostAsJsonAsync($"{Encounters}/{id}/transfers",
                new { destinationClinicId = hospitalB.Id }), HttpStatusCode.Conflict);
            // Reading does not start care.
            Assert.Equal(HttpStatusCode.OK, (await doctorA.Client.GetAsync($"{Encounters}/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await doctorA.Client.GetAsync($"{Encounters}/{id}/transfers")).StatusCode);
            Assert.Equal(ClinicalEncounterStatus.Received, await StatusAsync(host.Web, id));

            // A starts care: the first record moves it to InProgress; further records leave it there.
            Assert.Equal(HttpStatusCode.Created, (await nurseA.Client.PostAsJsonAsync($"{Encounters}/{id}/triages", new { priority = "VeryUrgent" })).StatusCode);
            Assert.Equal(ClinicalEncounterStatus.InProgress, await StatusAsync(host.Web, id));
            Assert.Equal(HttpStatusCode.Created, (await doctorA.Client.PostAsJsonAsync($"{Encounters}/{id}/notes",
                new { type = "Assessment", content = "Valoración en A (dato de prueba).", occurredAtUtc = DateTimeOffset.UtcNow.AddSeconds(-2) })).StatusCode);
            Assert.Equal(ClinicalEncounterStatus.InProgress, await StatusAsync(host.Web, id));

            // A -> B: destinations exclude A; request, then dispatch.
            var destinations = await (await doctorA.Client.GetAsync($"{Transfers}/destinations")).ReadAsAsync<List<TransferDestinationSummary>>();
            Assert.DoesNotContain(destinations, d => d.Id == hospitalA.Id);
            Assert.Contains(destinations, d => d.Id == hospitalB.Id && d.Name == "Hospital Receptor Demo");
            var request = await doctorA.Client.PostAsJsonAsync($"{Encounters}/{id}/transfers",
                new { destinationClinicId = hospitalB.Id, reason = "Requiere neurocirugía", notes = "Paciente intubado" });
            Assert.Equal(HttpStatusCode.Created, request.StatusCode);
            var toB = await request.ReadAsAsync<ClinicalTransferSummary>();
            Assert.Equal((ClinicalTransferStatus.Requested, hospitalA.Id, hospitalB.Id), (toB.Status, toB.OriginClinic!.Id, toB.DestinationClinic.Id));
            // One active transfer per encounter.
            await AssertProblemAsync(await doctorA.Client.PostAsJsonAsync($"{Encounters}/{id}/transfers",
                new { destinationClinicId = hospitalB.Id }), HttpStatusCode.Conflict);
            var dispatched = await doctorA.Client.PostAsync($"{Transfers}/{toB.Id}/dispatch", null);
            Assert.Equal(HttpStatusCode.OK, dispatched.StatusCode);
            Assert.Equal(ClinicalEncounterStatus.Transferred, (await dispatched.ReadAsAsync<ClinicalTransferSummary>()).EncounterStatus);
            await AssertProblemAsync(await doctorA.Client.PostAsync($"{Transfers}/{toB.Id}/dispatch", null), HttpStatusCode.Conflict);

            // The encounter's transfers at A: the outgoing one (newest) and the prehospital arrival.
            var atA = await (await doctorA.Client.GetAsync($"{Encounters}/{id}/transfers")).ReadAsAsync<List<ClinicalTransferSummary>>();
            Assert.Equal([toB.Id, prehospitalTransfer], atA.Select(t => t.Id));
            Assert.Equal([ClinicalTransferStatus.InTransit, ClinicalTransferStatus.Received], atA.Select(t => t.Status));

            // A cannot receive its own outgoing transfer; B receives it.
            await AssertProblemAsync(await doctorA.Client.PostAsync($"{Transfers}/{toB.Id}/receive", null), HttpStatusCode.Forbidden);
            Assert.Equal(HttpStatusCode.OK, (await doctorB.Client.PostAsync($"{Transfers}/{toB.Id}/receive", null)).StatusCode);
            await AssertProblemAsync(await doctorB.Client.PostAsync($"{Transfers}/{toB.Id}/receive", null), HttpStatusCode.Conflict);
            Assert.Equal(ClinicalEncounterStatus.Received, await StatusAsync(host.Web, id));

            // B continues the SAME encounter, with everything recorded before.
            var atB = await (await doctorB.Client.GetAsync($"{Encounters}/{id}")).ReadAsAsync<EncounterSummary>();
            Assert.Equal((id, emergency.TemporaryPatientCode, (Guid?)hospitalB.Id), (atB.Id, atB.TemporaryPatientCode, atB.ClinicId));
            Assert.Single(await (await doctorB.Client.GetAsync($"{Encounters}/{id}/triages")).ReadAsAsync<List<JsonElement>>());
            Assert.Single(await (await doctorB.Client.GetAsync($"{Encounters}/{id}/notes")).ReadAsAsync<List<JsonElement>>());
            var vitals = await (await doctorB.Client.GetAsync($"{Encounters}/{id}/vital-signs")).ReadAsAsync<List<JsonElement>>();
            Assert.Single(vitals); // the paramedic's heart rate
            // A handed the patient over and no longer reads it.
            await AssertProblemAsync(await doctorA.Client.GetAsync($"{Encounters}/{id}"), HttpStatusCode.Forbidden);

            Assert.Equal(HttpStatusCode.Created, (await doctorB.Client.PostAsJsonAsync($"{Encounters}/{id}/vital-signs",
                new { type = "HeartRate", value = 84, measuredAtUtc = DateTimeOffset.UtcNow.AddSeconds(-2) })).StatusCode);
            Assert.Equal(ClinicalEncounterStatus.InProgress, await StatusAsync(host.Web, id));

            Assert.Equal(HttpStatusCode.NoContent, (await doctorB.Client.PostAsJsonAsync($"{Encounters}/{id}/close",
                new { dischargeNote = "Alta a domicilio (dato de prueba)." })).StatusCode);
            Assert.Equal(ClinicalEncounterStatus.Closed, await StatusAsync(host.Web, id));

            var stored = await host.Web.DbAsync(db => db.ClinicalEncounters.AsNoTracking().SingleAsync(e => e.Id == id));
            Assert.Equal((hospitalB.Id, emergency.TemporaryPatientCode), (stored.ClinicId, stored.TemporaryPatientCode?.Value));
            Assert.Equal(2, await host.Web.DbAsync(db => db.ClinicalTransfers.CountAsync(t => t.ClinicalEncounterId == id && t.Status == ClinicalTransferStatus.Received)));
            Assert.Equal(2, await host.Web.DbAsync(db => db.VitalSigns.CountAsync(v => v.ClinicalEncounterId == id)));
            Assert.Equal(2, await host.Web.DbAsync(db => db.ClinicalNotes.CountAsync(n => n.ClinicalEncounterId == id)));
        }

        // The first care write and a concurrent change of the same Received encounter: the write loses as a whole. Neither
        // the record nor an InProgress without a record is ever stored.
        [SkippableFact]
        public async Task TheCareStartAndItsRecordAreOneTransaction()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var hospitalA = await host.Web.CreateClinicAsync("Hospital A");
            var doctorA = await host.Web.SignInAsync(SecurityRoles.Doctor, [hospitalA], staffType: ClinicalStaffType.Physician);
            var (emergency, _) = await ArrivedAtAsync(host, hospitalA, doctorA);
            var id = emergency.Id;
            var notesBefore = await host.Web.DbAsync(db => db.ClinicalNotes.CountAsync(n => n.ClinicalEncounterId == id));

            // A rejected record (out of range) changes nothing.
            await AssertProblemAsync(await doctorA.Client.PostAsJsonAsync($"{Encounters}/{id}/vital-signs",
                new { type = "HeartRate", value = 400, measuredAtUtc = DateTimeOffset.UtcNow }), HttpStatusCode.BadRequest);
            Assert.Equal(ClinicalEncounterStatus.Received, await StatusAsync(host.Web, id));

            // Received (4) -> InEvolution (5) committed by another transaction while the note request is in flight: the
            // request read Received, so its Received -> InProgress loses on the Status concurrency token.
            var (connection, change) = await ClinicalRace.BeginStatusChangeAsync(host.ConnectionString, id, from: 4, to: 5);
            await using var _ = connection;
            await using var __ = change;
            var write = doctorA.Client.PostAsJsonAsync($"{Encounters}/{id}/notes",
                new { type = "Evolution", content = "Nota en carrera (dato de prueba).", occurredAtUtc = DateTimeOffset.UtcNow });
            await ClinicalRace.WaitUntilBlockedOnLockAsync(host.ConnectionString, write);
            await change.CommitAsync();

            await AssertProblemAsync(await write, HttpStatusCode.Conflict);
            Assert.Equal(ClinicalEncounterStatus.InEvolution, await StatusAsync(host.Web, id));
            Assert.Equal(notesBefore, await host.Web.DbAsync(db => db.ClinicalNotes.CountAsync(n => n.ClinicalEncounterId == id)));
        }

        [SkippableFact]
        public async Task DestinationsAreForActiveClinicalStaffWithTransfersManageOnly()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await ClinicalApiHost.StartAsync(_postgres);
            var hospitalA = await host.CreateClinicAsync("Hospital A");
            var hospitalB = await host.CreateClinicAsync("Hospital B");
            var closed = await host.CreateClinicAsync("Hospital cerrado");
            await host.DbAsync(async db =>
            {
                var clinic = await db.Clinics.SingleAsync(c => c.Id == closed.Id);
                clinic.Deactivate();
                await db.SaveChangesAsync();
            });
            var doctorA = await host.SignInAsync(SecurityRoles.Doctor, [hospitalA], staffType: ClinicalStaffType.Physician);
            var twoClinics = await host.SignInAsync(SecurityRoles.Doctor, [hospitalA, hospitalB], staffType: ClinicalStaffType.Physician);
            var nurse = await host.SignInAsync(SecurityRoles.Nurse, [hospitalA], staffType: ClinicalStaffType.Nurse);
            var admin = await host.SignInAsync(SecurityRoles.Administrator, [], asClinicalStaff: false);
            var inactive = await host.SignInAsync(SecurityRoles.Doctor, [hospitalA], staffActive: false);

            var destinations = await (await doctorA.Client.GetAsync($"{Transfers}/destinations")).ReadAsAsync<List<TransferDestinationSummary>>();

            Assert.Equal([hospitalB.Id], destinations.Select(d => d.Id));
            Assert.Equal(hospitalB.Id, Assert.Single(await (await twoClinics.Client.GetAsync($"{Transfers}/destinations?clinicId={hospitalA.Id}"))
                .ReadAsAsync<List<TransferDestinationSummary>>()).Id);
            await AssertProblemAsync(await twoClinics.Client.GetAsync($"{Transfers}/destinations"), HttpStatusCode.BadRequest);
            await AssertProblemAsync(await doctorA.Client.GetAsync($"{Transfers}/destinations?clinicId={hospitalB.Id}"), HttpStatusCode.Forbidden);
            // Transfers.Read is not enough, the administrator does not bypass the clinical actor, inactive staff is out.
            await AssertProblemAsync(await nurse.Client.GetAsync($"{Transfers}/destinations"), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await admin.Client.GetAsync($"{Transfers}/destinations"), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await inactive.Client.GetAsync($"{Transfers}/destinations"), HttpStatusCode.Forbidden);
            // Only the transfer projection: no admin clinic data.
            var body = await (await doctorA.Client.GetAsync($"{Transfers}/destinations")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("status", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("createdAt", body, StringComparison.OrdinalIgnoreCase);
        }

        [SkippableFact]
        public async Task EncounterTransfersAreReadableOnlyLikeTheEncounter()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var hospitalA = await host.Web.CreateClinicAsync("Hospital A");
            var other = await host.Web.CreateClinicAsync("Otra clínica");
            var doctorA = await host.Web.SignInAsync(SecurityRoles.Doctor, [hospitalA], staffType: ClinicalStaffType.Physician);
            var nurseA = await host.Web.SignInAsync(SecurityRoles.Nurse, [hospitalA], staffType: ClinicalStaffType.Nurse);
            var foreign = await host.Web.SignInAsync(SecurityRoles.Doctor, [other], staffType: ClinicalStaffType.Physician);
            var reception = await host.Web.SignInAsync(SecurityRoles.Receptionist, [hospitalA]);
            var (emergency, prehospitalTransfer) = await ArrivedAtAsync(host, hospitalA, doctorA);

            var history = await (await nurseA.Client.GetAsync($"{Encounters}/{emergency.Id}/transfers")).ReadAsAsync<List<ClinicalTransferSummary>>();

            var arrival = Assert.Single(history);
            Assert.Equal((prehospitalTransfer, ClinicalTransferStatus.Received, (ClinicSummary?)null, hospitalA.Id),
                (arrival.Id, arrival.Status, arrival.OriginClinic, arrival.DestinationClinic.Id));
            await AssertProblemAsync(await foreign.Client.GetAsync($"{Encounters}/{emergency.Id}/transfers"), HttpStatusCode.Forbidden);
            // Reception has Transfers.Read but not Encounters.Read: the clinical list of an encounter is not for it.
            await AssertProblemAsync(await reception.Client.GetAsync($"{Encounters}/{emergency.Id}/transfers"), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await doctorA.Client.GetAsync($"{Encounters}/{Guid.NewGuid()}/transfers"), HttpStatusCode.NotFound);
        }
    }
}
