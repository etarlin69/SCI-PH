using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // The procedures the paramedic recorded (Mobile BFF) are read by the hospital that holds the SAME encounter
    // (GET /api/web/encounters/{id}/procedures) over PostgreSQL: not before it receives the patient, never by another
    // clinic, and they follow the encounter to the next hospital; nothing is copied.
    [Collection(PostgresCollection.Name)]
    public sealed class HospitalPrehospitalProceduresApiTests
    {
        private const string Emergencies = "/api/mobile/emergencies";
        private const string Encounters = "/api/web/encounters";
        private const string Transfers = "/api/web/transfers";
        private readonly PostgresFixture _postgres;

        public HospitalPrehospitalProceduresApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static DateTimeOffset NowToTheMillisecond()
        {
            var now = DateTimeOffset.UtcNow;
            return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        }

        private static string Procedures(Guid encounterId) => $"{Encounters}/{encounterId}/procedures";

        private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.DoesNotContain("Oxigenoterapia", await response.Content.ReadAsStringAsync());
        }

        // An NN emergency with two procedures, in transport to the hospital (not received yet).
        private static async Task<(PrehospitalEmergencySummary Emergency, Guid TransferId)> InTransportAsync(MobileEmergencyHost host, Clinic hospital)
        {
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync("Base ambulancias")]);
            var now = NowToTheMillisecond();
            var opened = await paramedic.Client.PostAsJsonAsync(Emergencies,
                new { clientRequestId = Guid.NewGuid(), startedAt = now.AddMinutes(-20), chiefComplaint = "Disnea" });
            Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
            var emergency = await opened.ReadAsAsync<PrehospitalEmergencySummary>();
            foreach (var (code, performedAt, outcome) in new[] { ("OXYGEN-THERAPY", now.AddMinutes(-15), "SpO2 97 %"), ("PERIPHERAL-IV-ACCESS", now.AddMinutes(-10), (string?)null) })
            {
                Assert.Equal(HttpStatusCode.Created, (await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{emergency.Id}/procedures",
                    new { clientRequestId = Guid.NewGuid(), procedureCode = code, performedAt, outcome })).StatusCode);
            }

            var requested = await (await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{emergency.Id}/transfers",
                new { clientRequestId = Guid.NewGuid(), destinationClinicId = hospital.Id })).ReadAsAsync<EmergencyTransferSummary>();
            Assert.Equal(HttpStatusCode.OK, (await paramedic.Client.PostAsync($"{Emergencies}/{emergency.Id}/transfers/{requested.Id}/start", null)).StatusCode);
            return (emergency, requested.Id);
        }

        [SkippableFact]
        public async Task TheReceivingHospitalSeesTheParamedicsProceduresInTheSameEncounterAndTheyFollowIt()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var hospitalA = await host.Web.CreateClinicAsync("Hospital A");
            var hospitalB = await host.Web.CreateClinicAsync("Hospital B");
            var doctorA = await host.Web.SignInAsync(SecurityRoles.Doctor, [hospitalA], staffType: ClinicalStaffType.Physician);
            var nurseA = await host.Web.SignInAsync(SecurityRoles.Nurse, [hospitalA], staffType: ClinicalStaffType.Nurse);
            var doctorB = await host.Web.SignInAsync(SecurityRoles.Doctor, [hospitalB], staffType: ClinicalStaffType.Physician);
            var (emergency, transferId) = await InTransportAsync(host, hospitalA);
            var id = emergency.Id;

            // On the way the receiving hospital does not read the encounter yet, nor its procedures.
            await AssertProblemAsync(await doctorA.Client.GetAsync(Procedures(id)), HttpStatusCode.Forbidden);

            Assert.Equal(HttpStatusCode.OK, (await doctorA.Client.PostAsync($"{Transfers}/{transferId}/receive", null)).StatusCode);
            var atA = await doctorA.Client.GetAsync(Procedures(id));
            Assert.Equal(HttpStatusCode.OK, atA.StatusCode);
            var procedures = await atA.ReadAsAsync<List<ClinicalProcedureSummary>>();
            Assert.Equal(["Acceso venoso periférico", "Oxigenoterapia"], procedures.Select(p => p.Name));
            Assert.All(procedures, p =>
            {
                Assert.Equal(id, p.EncounterId);
                Assert.True(p.Prehospital);
                Assert.Equal(PrehospitalProcedureCatalog.CodeSystem, p.CodeSystem);
                Assert.Equal(ClinicalProcedureStatus.Completed, p.Status);
                Assert.False(string.IsNullOrWhiteSpace(p.PerformedByDisplayName));
            });
            Assert.Equal("SpO2 97 %", procedures[1].Outcome);
            // The contract carries the origin and the performer's name, not the staff id.
            using (var json = JsonDocument.Parse(await atA.Content.ReadAsStringAsync()))
            {
                var first = json.RootElement[0];
                Assert.True(first.GetProperty("prehospital").GetBoolean());
                Assert.False(first.TryGetProperty("performedByStaffId", out _));
            }

            // Same encounter and NN code; reading again (a reload) gives the same list.
            var encounter = await (await doctorA.Client.GetAsync($"{Encounters}/{id}")).ReadAsAsync<EncounterSummary>();
            Assert.Equal((id, emergency.TemporaryPatientCode), (encounter.Id, encounter.TemporaryPatientCode));
            Assert.Equal(procedures.Select(p => p.Id), (await (await doctorA.Client.GetAsync(Procedures(id))).ReadAsAsync<List<ClinicalProcedureSummary>>()).Select(p => p.Id));

            // Another clinic never reads them.
            await AssertProblemAsync(await doctorB.Client.GetAsync(Procedures(id)), HttpStatusCode.Forbidden);

            // A starts care and sends the patient on to B: B reads the SAME procedures, A no longer does.
            Assert.Equal(HttpStatusCode.Created, (await nurseA.Client.PostAsJsonAsync($"{Encounters}/{id}/triages", new { priority = "Urgent" })).StatusCode);
            var toB = await (await doctorA.Client.PostAsJsonAsync($"{Encounters}/{id}/transfers", new { destinationClinicId = hospitalB.Id }))
                .ReadAsAsync<ClinicalTransferSummary>();
            Assert.Equal(HttpStatusCode.OK, (await doctorA.Client.PostAsync($"{Transfers}/{toB.Id}/dispatch", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await doctorB.Client.PostAsync($"{Transfers}/{toB.Id}/receive", null)).StatusCode);

            var atB = await (await doctorB.Client.GetAsync(Procedures(id))).ReadAsAsync<List<ClinicalProcedureSummary>>();
            Assert.Equal(procedures.Select(p => p.Id), atB.Select(p => p.Id));
            await AssertProblemAsync(await doctorA.Client.GetAsync(Procedures(id)), HttpStatusCode.Forbidden);

            // Nothing was copied: one encounter, two procedure rows.
            Assert.Equal(2, await host.Web.DbAsync(db => db.ClinicalProcedures.CountAsync(p => p.ClinicalEncounterId == id)));
            Assert.Equal(1, await host.Web.DbAsync(db => db.ClinicalEncounters.CountAsync(e => e.Id == id)));
        }

        [SkippableFact]
        public async Task ReadingProceduresNeedsEncounterReadAndAnExistingEncounter()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var hospital = await host.Web.CreateClinicAsync("Hospital A");
            var doctor = await host.Web.SignInAsync(SecurityRoles.Doctor, [hospital], staffType: ClinicalStaffType.Physician);
            var reception = await host.Web.SignInAsync(SecurityRoles.Receptionist, [hospital]);
            var (emergency, transferId) = await InTransportAsync(host, hospital);
            Assert.Equal(HttpStatusCode.OK, (await doctor.Client.PostAsync($"{Transfers}/{transferId}/receive", null)).StatusCode);

            // Reception has no clinical read; an unknown encounter is a 404; without a token, 401.
            await AssertProblemAsync(await reception.Client.GetAsync(Procedures(emergency.Id)), HttpStatusCode.Forbidden);
            await AssertProblemAsync(await doctor.Client.GetAsync(Procedures(Guid.NewGuid())), HttpStatusCode.NotFound);
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Web.Anonymous().GetAsync(Procedures(emergency.Id))).StatusCode);
            // Read-only on the web: procedures are recorded by the paramedic channel.
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await doctor.Client.PostAsJsonAsync(Procedures(emergency.Id), new { name = "Sutura" })).StatusCode);
        }
    }
}
