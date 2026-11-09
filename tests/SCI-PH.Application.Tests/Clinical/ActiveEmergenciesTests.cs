using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.Enums.Transport;

namespace SCI_PH.Application.Tests.Clinical
{
    // The paramedic's active list (PrehospitalEmergencyService.ListActiveAsync): the cases the current paramedic can still
    // continue in the field (assigned, prehospital, no clinic yet, recordable status), newest first, enforced by the query.
    public class ActiveEmergenciesTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        private static async Task<ClinicalEncounter> OpenAsync(ClinicalHarness harness, int minutesAgo = 10, string complaint = "Politrauma")
        {
            var opened = await harness.Emergencies.OpenAsync(
                new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), ClinicalHarness.Start.AddMinutes(-minutesAgo), complaint), None);
            return harness.Store.Encounters.Single(e => e.Id == opened.Emergency.Id);
        }

        private static Guid SeedDestination(ClinicalHarness harness)
        {
            var id = Guid.NewGuid();
            harness.Store.Clinics[id] = ClinicStatus.Active;
            harness.Store.ClinicDetails[id] = ("Hospital Central", ClinicType.Hospital, "Santo Domingo Este", "Santo Domingo");
            return id;
        }

        private static ClinicalHarness OtherParamedic(ClinicalHarness harness)
        {
            var other = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic), harness.Store);
            other.Staff.Add(other.User.UserId!.Value, true, Guid.NewGuid());
            return other;
        }

        private static ClinicalHarness Hospital(ClinicalHarness paramedic, Guid destination)
        {
            var hospital = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Doctor), paramedic.Store);
            hospital.Staff.Add(hospital.User.UserId!.Value, true, destination);
            return hospital;
        }

        private static async Task<EmergencyTransferSummary> RequestTransferAsync(ClinicalHarness harness, ClinicalEncounter emergency, Guid destination) =>
            (await harness.Emergencies.RequestTransferAsync(
                emergency.Id, new RequestEmergencyTransferRequest(Guid.NewGuid(), destination, "Trauma mayor"), None)).Transfer;

        [Fact]
        public async Task WithoutEmergenciesTheListIsEmpty()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);

            Assert.Empty(await harness.Emergencies.ListActiveAsync(None));
        }

        [Fact]
        public async Task AnUnidentifiedCaseIsListedWithItsNnCodeAndNoPatient()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var emergency = await OpenAsync(harness, complaint: "Dolor torácico");

            var item = Assert.Single(await harness.Emergencies.ListActiveAsync(None));

            Assert.Equal(new ActivePrehospitalEmergencySummary(
                emergency.Id, emergency.TemporaryPatientCode!.Value, null, ClinicalEncounterStatus.Created,
                ClinicalHarness.Start.AddMinutes(-10), "Dolor torácico", null), item);
        }

        [Fact]
        public async Task AnIdentifiedCaseShowsThePatientNameAndKeepsTheNnCode()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            harness.SeedPatient();
            var emergency = await OpenAsync(harness);
            await harness.Emergencies.IdentifyByDocumentAsync(
                emergency.Id, new IdentifyEmergencyByDocumentRequest(Guid.NewGuid(), PatientIdentifierType.NationalId, "001-1234567-8"), None);

            var item = Assert.Single(await harness.Emergencies.ListActiveAsync(None));

            Assert.Equal(("Ana Pérez", emergency.TemporaryPatientCode!.Value), (item.PatientDisplayName, item.TemporaryPatientCode));
        }

        [Fact]
        public async Task TheNewestCaseComesFirst()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var older = await OpenAsync(harness, minutesAgo: 60);
            var newest = await OpenAsync(harness, minutesAgo: 5);
            var middle = await OpenAsync(harness, minutesAgo: 30);

            var list = await harness.Emergencies.ListActiveAsync(None);

            Assert.Equal([newest.Id, middle.Id, older.Id], list.Select(e => e.Id));
        }

        [Fact]
        public async Task AnotherParamedicsCasesAreNeverListed()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var mine = await OpenAsync(harness);
            var other = OtherParamedic(harness);
            var theirs = await OpenAsync(other);

            Assert.Equal([mine.Id], (await harness.Emergencies.ListActiveAsync(None)).Select(e => e.Id));
            Assert.Equal([theirs.Id], (await other.Emergencies.ListActiveAsync(None)).Select(e => e.Id));
        }

        // The list follows the assignment, not the creator: an ended assignment leaves the case out.
        [Fact]
        public async Task AnEndedAssignmentLeavesTheCaseOut()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var emergency = await OpenAsync(harness);
            harness.Store.Assignments.Single(a => a.ClinicalEncounterId == emergency.Id).Unassign(ClinicalHarness.Start);

            Assert.Empty(await harness.Emergencies.ListActiveAsync(None));
        }

        [Fact]
        public async Task ARequestedOrInTransitTransferIsShownAndTheCaseStaysActive()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var emergency = await OpenAsync(harness);
            var destination = SeedDestination(harness);
            var transfer = await RequestTransferAsync(harness, emergency, destination);

            var requested = Assert.Single(await harness.Emergencies.ListActiveAsync(None));
            Assert.Equal((ClinicalEncounterStatus.InProgress, (ClinicalTransferStatus?)ClinicalTransferStatus.Requested),
                (requested.Status, requested.ActiveTransferStatus));

            await harness.Emergencies.StartTransferAsync(emergency.Id, transfer.Id, None);

            var inTransit = Assert.Single(await harness.Emergencies.ListActiveAsync(None));
            Assert.Equal((ClinicalEncounterStatus.Transferred, (ClinicalTransferStatus?)ClinicalTransferStatus.InTransit),
                (inTransit.Status, inTransit.ActiveTransferStatus));
        }

        [Fact]
        public async Task ACancelledTransferIsNoLongerShown()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var emergency = await OpenAsync(harness);
            var transfer = await RequestTransferAsync(harness, emergency, SeedDestination(harness));
            await harness.Emergencies.CancelTransferAsync(emergency.Id, transfer.Id, None);

            var item = Assert.Single(await harness.Emergencies.ListActiveAsync(None));

            Assert.Equal((ClinicalEncounterStatus.InProgress, (ClinicalTransferStatus?)null), (item.Status, item.ActiveTransferStatus));
        }

        // Once the destination received it, the paramedic no longer writes on it (409), so it leaves the actionable list,
        // although the assignment row is still open.
        [Fact]
        public async Task AHandedOffCaseLeavesTheListAlthoughTheAssignmentIsStillOpen()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var emergency = await OpenAsync(harness);
            var kept = await OpenAsync(harness, minutesAgo: 20);
            var destination = SeedDestination(harness);
            var transfer = await RequestTransferAsync(harness, emergency, destination);
            await harness.Emergencies.StartTransferAsync(emergency.Id, transfer.Id, None);

            await Hospital(harness, destination).Transfers.ReceiveAsync(transfer.Id, None);

            Assert.True(harness.Store.Assignments.Single(a => a.ClinicalEncounterId == emergency.Id).IsActive);
            Assert.Equal([kept.Id], (await harness.Emergencies.ListActiveAsync(None)).Select(e => e.Id));
            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.RecordNoteAsync(
                emergency.Id, new RecordEmergencyNoteRequest(Guid.NewGuid(), ClinicalHarness.Start, "Tarde."), None));
        }

        // Only statuses in which the paramedic still records care (PrehospitalRecordableStatuses).
        [Theory]
        [InlineData(ClinicalEncounterStatus.Cancelled)]
        [InlineData(ClinicalEncounterStatus.Closed)]
        public async Task AFinishedCaseIsNotListed(ClinicalEncounterStatus finished)
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var emergency = await OpenAsync(harness);
            emergency.ChangeStatus(ClinicalEncounterStatus.InProgress, ClinicalHarness.Start);
            emergency.ChangeStatus(finished, ClinicalHarness.Start);

            Assert.Empty(await harness.Emergencies.ListActiveAsync(None));
        }

        // Clinic isolation: an encounter of a clinic is never an emergency of the list, even with an open assignment.
        [Fact]
        public async Task AClinicEncounterIsNeverListedEvenWhenAssigned()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var hospital = harness.SeedEncounter();
            harness.Store.Assignments.Add(EncounterStaffAssignment.Assign(
                hospital.Id, harness.StaffId, EncounterStaffFunction.Paramedic, ClinicalHarness.Start));

            Assert.Empty(await harness.Emergencies.ListActiveAsync(None));
        }

        [Fact]
        public async Task OnlyActiveClinicalStaffGetTheList()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            await OpenAsync(harness);
            var noStaff = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic), harness.Store);

            await Assert.ThrowsAsync<AccessDeniedException>(() => noStaff.Emergencies.ListActiveAsync(None));
        }
    }
}
