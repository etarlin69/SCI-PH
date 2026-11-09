using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.Enums.Transport;
using SCI_PH.IntegrationTests.Clinical.Support;
using SCI_PH.IntegrationTests.PostgreSql;
using SCI_PH.IntegrationTests.Security.Support;

namespace SCI_PH.IntegrationTests.Clinical.Http
{
    // GET api/mobile/emergencies/active over PostgreSQL: the current paramedic's actionable emergencies (assigned, still in
    // the field), newest first, with the NN code, the patient name once identified and the active transfer's status; never
    // another paramedic's cases, and a case leaves the list once the destination received it.
    [Collection(PostgresCollection.Name)]
    public sealed class MobileActiveEmergenciesApiTests
    {
        private const string Emergencies = "/api/mobile/emergencies";
        private const string Active = "/api/mobile/emergencies/active";
        private readonly PostgresFixture _postgres;

        public MobileActiveEmergenciesApiTests(PostgresFixture postgres) => _postgres = postgres;

        private static DateTimeOffset NowToTheMillisecond()
        {
            var now = DateTimeOffset.UtcNow;
            return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        }

        private static async Task<PrehospitalEmergencySummary> OpenAsync(StaffSession paramedic, int minutesAgo = 10, string complaint = "Politrauma")
        {
            var response = await paramedic.Client.PostAsJsonAsync(Emergencies,
                new { clientRequestId = Guid.NewGuid(), startedAt = NowToTheMillisecond().AddMinutes(-minutesAgo), chiefComplaint = complaint });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await response.ReadAsAsync<PrehospitalEmergencySummary>();
        }

        private static async Task<List<ActivePrehospitalEmergencySummary>> ListAsync(StaffSession paramedic)
        {
            var response = await paramedic.Client.GetAsync(Active);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadAsAsync<List<ActivePrehospitalEmergencySummary>>();
        }

        private static async Task<EmergencyTransferSummary> RequestTransferAsync(StaffSession paramedic, Guid emergencyId, Guid destination)
        {
            var response = await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{emergencyId}/transfers",
                new { clientRequestId = Guid.NewGuid(), destinationClinicId = destination, reason = "Trauma mayor" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await response.ReadAsAsync<EmergencyTransferSummary>();
        }

        [SkippableFact]
        public async Task WithoutEmergenciesTheListIsAnEmptyArray()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync("Base")]);

            var response = await paramedic.Client.GetAsync(Active);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("[]", await response.Content.ReadAsStringAsync());
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        }

        // Only the summary the list needs: no PatientId, no patient summary, no clinic or staff.
        [SkippableFact]
        public async Task TheListShowsOnlyThePublicSummaryNewestFirst()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync("Base")]);
            var older = await OpenAsync(paramedic, minutesAgo: 40, complaint: "Caída de altura");
            var newest = await OpenAsync(paramedic, minutesAgo: 5, complaint: "Dolor torácico");

            var response = await paramedic.Client.GetAsync(Active);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var items = json.RootElement.EnumerateArray().ToList();
            Assert.Equal([newest.Id, older.Id], items.Select(i => i.GetProperty("id").GetGuid()));
            Assert.All(items, i => Assert.Equal(
                ["id", "temporaryPatientCode", "patientDisplayName", "status", "startedAtUtc", "chiefComplaint", "activeTransferStatus"],
                i.EnumerateObject().Select(p => p.Name)));
            var first = items[0];
            Assert.Equal((newest.TemporaryPatientCode, JsonValueKind.Null, "Created", "Dolor torácico", JsonValueKind.Null),
                (first.GetProperty("temporaryPatientCode").GetString(), first.GetProperty("patientDisplayName").ValueKind,
                 first.GetProperty("status").GetString(), first.GetProperty("chiefComplaint").GetString(),
                 first.GetProperty("activeTransferStatus").ValueKind));
        }

        [SkippableFact]
        public async Task AnotherParamedicsCasesAreNotReturnedAndTheirDirectGetStaysForbidden()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var mine = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            // Same organization and base clinic: still not my case.
            var colleague = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var own = await OpenAsync(mine);
            var theirs = await OpenAsync(colleague);

            Assert.Equal([own.Id], (await ListAsync(mine)).Select(e => e.Id));
            Assert.Equal([theirs.Id], (await ListAsync(colleague)).Select(e => e.Id));
            Assert.Equal(HttpStatusCode.Forbidden, (await mine.Client.GetAsync($"{Emergencies}/{theirs.Id}")).StatusCode);
        }

        // Identified: the patient's name, with the NN code kept as traceability of the same encounter.
        [SkippableFact]
        public async Task AnIdentifiedCaseShowsThePatientNameAndKeepsTheNnCode()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [await host.Web.CreateClinicAsync("Base")]);
            var seeded = Patient.Register("Marta", "Gómez", new DateOnly(1985, 3, 14), BiologicalSex.Female);
            seeded.AddIdentifier(PatientIdentifierType.NationalId, "402-7654321-0", PatientIdentifierSource.Manual, null, isPrimary: true);
            await host.Web.DbAsync(async db =>
            {
                db.Patients.Add(seeded);
                await db.SaveChangesAsync();
            });
            var opened = await OpenAsync(paramedic);
            var identified = await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{opened.Id}/identify/document",
                new { clientRequestId = Guid.NewGuid(), documentType = "NationalId", documentNumber = "402-7654321-0" });
            Assert.Equal(HttpStatusCode.OK, identified.StatusCode);
            var patient = (await identified.ReadAsAsync<PrehospitalEmergencySummary>()).Patient!;

            var item = Assert.Single(await ListAsync(paramedic));

            Assert.Equal((opened.Id, opened.TemporaryPatientCode, "Marta Gómez", patient.DisplayName),
                (item.Id, item.TemporaryPatientCode, item.PatientDisplayName, item.PatientDisplayName));
        }

        // Requested and InTransit keep the case active with its badge; once received it is no longer actionable for the
        // paramedic (409 on any write) and leaves the list, although the assignment row still exists.
        [SkippableFact]
        public async Task AfterTheHandoffTheCaseIsNoLongerListed()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var hospital = await host.Web.CreateClinicAsync("Hospital receptor");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            var doctor = await host.Web.SignInAsync(SecurityRoles.Doctor, [hospital], staffType: ClinicalStaffType.Physician);
            var opened = await OpenAsync(paramedic);
            var kept = await OpenAsync(paramedic, minutesAgo: 30);

            var transfer = await RequestTransferAsync(paramedic, opened.Id, hospital.Id);
            var requested = (await ListAsync(paramedic)).Single(e => e.Id == opened.Id);
            Assert.Equal((ClinicalEncounterStatus.InProgress, (ClinicalTransferStatus?)ClinicalTransferStatus.Requested),
                (requested.Status, requested.ActiveTransferStatus));

            Assert.Equal(HttpStatusCode.OK, (await paramedic.Client.PostAsync($"{Emergencies}/{opened.Id}/transfers/{transfer.Id}/start", null)).StatusCode);
            var inTransit = (await ListAsync(paramedic)).Single(e => e.Id == opened.Id);
            Assert.Equal((ClinicalEncounterStatus.Transferred, (ClinicalTransferStatus?)ClinicalTransferStatus.InTransit),
                (inTransit.Status, inTransit.ActiveTransferStatus));

            Assert.Equal(HttpStatusCode.OK, (await doctor.Client.PostAsync($"/api/web/transfers/{transfer.Id}/receive", null)).StatusCode);

            Assert.Equal([kept.Id], (await ListAsync(paramedic)).Select(e => e.Id));
            Assert.True(await host.Web.DbAsync(db => db.EncounterStaffAssignments.AnyAsync(
                a => a.ClinicalEncounterId == opened.Id && a.ClinicalStaffId == paramedic.StaffId && a.UnassignedAtUtc == null)));
            var note = await paramedic.Client.PostAsJsonAsync($"{Emergencies}/{opened.Id}/notes",
                new { clientRequestId = Guid.NewGuid(), occurredAt = NowToTheMillisecond(), content = "Tarde." });
            Assert.Equal(HttpStatusCode.Conflict, note.StatusCode);
        }

        [SkippableFact]
        public async Task TheListNeedsAnAuthenticatedActiveParamedic()
        {
            Skip.IfNot(_postgres.Available, _postgres.SkipReason);
            await using var host = await MobileEmergencyHost.StartAsync(_postgres);
            var baseClinic = await host.Web.CreateClinicAsync("Base");
            var paramedic = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic]);
            await OpenAsync(paramedic);

            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Anonymous().GetAsync(Active)).StatusCode);
            // No Emergencies.Operate: the module is closed.
            var nurse = await host.SignInAsync(SecurityRoles.Nurse, [baseClinic], staffType: ClinicalStaffType.Nurse);
            Assert.Equal(HttpStatusCode.Forbidden, (await nurse.Client.GetAsync(Active)).StatusCode);
            // The role but no active clinical staff record.
            var inactive = await host.SignInAsync(SecurityRoles.Paramedic, [baseClinic], staffActive: false);
            Assert.Equal(HttpStatusCode.Forbidden, (await inactive.Client.GetAsync(Active)).StatusCode);

            // The clinical staff record is disabled while the session is still open: the next list is a 403.
            await host.Web.DbAsync(async db =>
            {
                (await db.ClinicalStaffMembers.SingleAsync(s => s.Id == paramedic.StaffId)).Deactivate();
                await db.SaveChangesAsync();
            });
            Assert.Equal(HttpStatusCode.Forbidden, (await paramedic.Client.GetAsync(Active)).StatusCode);
        }
    }
}
