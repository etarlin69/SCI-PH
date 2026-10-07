using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Application.Tests.Clinical
{
    public class EncounterRecordTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        [Fact]
        public async Task FirstTriageMovesCreatedToInProgressInTheSameSave()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Nurse);
            var encounter = harness.SeedEncounter();
            harness.Time.Now = ClinicalHarness.Start.AddMinutes(10);

            var triage = await harness.Records.RecordTriageAsync(encounter.Id, new RecordTriageRequest(
                TriagePriority.Urgent,
                Measurements: [new VitalSignMeasurementInput(VitalSignType.HeartRate, 110, harness.Time.Now.AddMinutes(-1))]), None);

            Assert.Equal(ClinicalEncounterStatus.InProgress, encounter.Status);
            Assert.Equal(1, harness.Store.SaveCount);
            var vital = Assert.Single(harness.Store.VitalSigns);
            Assert.Equal(triage.Id, vital.TriageId);
            Assert.Equal("/min", vital.Unit);
            Assert.Equal(harness.Time.Now, triage.PerformedAtUtc);
        }

        [Fact]
        public async Task EncounterAlreadyInProgressIsNotTransitionedAgain()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Nurse);
            var encounter = harness.SeedEncounter();
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, ClinicalHarness.Start);
            var updatedAt = encounter.UpdatedAtUtc;
            harness.Time.Now = ClinicalHarness.Start.AddMinutes(5);

            await harness.Records.RecordVitalSignAsync(encounter.Id,
                new RecordVitalSignRequest(VitalSignType.Temperature, 37.5m, harness.Time.Now), None);

            Assert.Equal(ClinicalEncounterStatus.InProgress, encounter.Status);
            Assert.Equal(updatedAt, encounter.UpdatedAtUtc);
        }

        [Fact]
        public async Task VitalSignMovesCreatedToInProgress()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Nurse);
            var encounter = harness.SeedEncounter();

            var vital = await harness.Records.RecordVitalSignAsync(encounter.Id,
                new RecordVitalSignRequest(VitalSignType.OxygenSaturation, 97, ClinicalHarness.Start), None);

            Assert.Equal(ClinicalEncounterStatus.InProgress, encounter.Status);
            Assert.Equal("%", vital.Unit);
            Assert.Null(vital.TriageId);
            Assert.Equal((await harness.Actor.GetAsync(None)).StaffId, vital.MeasuredByStaffId);
        }

        [Theory]
        [InlineData(ClinicalEncounterStatus.Closed)]
        [InlineData(ClinicalEncounterStatus.Cancelled)]
        [InlineData(ClinicalEncounterStatus.Transferred)]
        public async Task ClosedOrIncompatibleEncounterIsAConflict(ClinicalEncounterStatus status)
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Nurse);
            var encounter = harness.SeedEncounter();
            if (status != ClinicalEncounterStatus.Cancelled)
            {
                encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, ClinicalHarness.Start);
            }

            encounter.ChangeStatus(status, ClinicalHarness.Start);

            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                harness.Records.RecordTriageAsync(encounter.Id, new RecordTriageRequest(TriagePriority.Standard), None));
            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Records.RecordVitalSignAsync(encounter.Id,
                new RecordVitalSignRequest(VitalSignType.HeartRate, 80, ClinicalHarness.Start), None));
            Assert.Equal(0, harness.Store.SaveCount);
        }

        [Fact]
        public async Task FutureDatesAreRejected()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Nurse);
            var encounter = harness.SeedEncounter();
            var future = ClinicalHarness.Start.AddMinutes(1);

            var triage = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Records.RecordTriageAsync(
                encounter.Id, new RecordTriageRequest(TriagePriority.Urgent, PerformedAtUtc: future), None));
            var vital = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Records.RecordVitalSignAsync(
                encounter.Id, new RecordVitalSignRequest(VitalSignType.HeartRate, 80, future), None));
            var measurement = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Records.RecordTriageAsync(
                encounter.Id, new RecordTriageRequest(TriagePriority.Urgent,
                    Measurements: [new VitalSignMeasurementInput(VitalSignType.HeartRate, 80, future)]), None));

            Assert.Equal("performedAtUtc", triage.Field);
            Assert.Equal("measuredAtUtc", vital.Field);
            Assert.Equal("measurements[0].measuredAtUtc", measurement.Field);
            Assert.Equal(ClinicalEncounterStatus.Created, encounter.Status);
        }

        [Fact]
        public async Task DatesBeforeTheEncounterStartAreRejected()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Nurse);
            var encounter = harness.SeedEncounter();
            var before = ClinicalHarness.Start.AddSeconds(-1);

            await Assert.ThrowsAsync<RequestValidationException>(() => harness.Records.RecordVitalSignAsync(
                encounter.Id, new RecordVitalSignRequest(VitalSignType.HeartRate, 80, before), None));
            await Assert.ThrowsAsync<RequestValidationException>(() => harness.Records.RecordTriageAsync(
                encounter.Id, new RecordTriageRequest(TriagePriority.Urgent, PerformedAtUtc: before), None));
        }

        [Fact]
        public async Task OutOfRangeValueIsADomainRuleAndNothingIsSaved()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Nurse);
            var encounter = harness.SeedEncounter();

            await Assert.ThrowsAsync<DomainException>(() => harness.Records.RecordVitalSignAsync(
                encounter.Id, new RecordVitalSignRequest(VitalSignType.HeartRate, 301, ClinicalHarness.Start), None));
            await Assert.ThrowsAsync<DomainException>(() => harness.Records.RecordTriageAsync(encounter.Id, new RecordTriageRequest(
                TriagePriority.Urgent, Measurements: [new VitalSignMeasurementInput(VitalSignType.Temperature, 50, ClinicalHarness.Start)]), None));

            Assert.Equal(ClinicalEncounterStatus.Created, encounter.Status);
            Assert.Equal(0, harness.Store.SaveCount);
        }

        [Fact]
        public async Task MeasurementsInsideATriageNeedVitalSignsWrite()
        {
            var user = new FakeCurrentUser(Guid.NewGuid(), [SecurityRoles.Nurse], [Permissions.Triage.Write, Permissions.Encounters.Read]);
            var harness = new ClinicalHarness(user);
            harness.Staff.Add(user.UserId!.Value, true, Guid.NewGuid());
            var encounter = harness.SeedEncounter();

            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Records.RecordTriageAsync(encounter.Id, new RecordTriageRequest(
                TriagePriority.Urgent, Measurements: [new VitalSignMeasurementInput(VitalSignType.HeartRate, 80, ClinicalHarness.Start)]), None));
            await harness.Records.RecordTriageAsync(encounter.Id, new RecordTriageRequest(TriagePriority.Urgent), None);
        }

        [Fact]
        public async Task RecordingAtAnotherClinicIsDenied()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Nurse);
            var foreign = harness.SeedEncounter(clinicId: Guid.NewGuid());

            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                harness.Records.RecordTriageAsync(foreign.Id, new RecordTriageRequest(TriagePriority.Urgent), None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                harness.Records.RecordTriageAsync(Guid.NewGuid(), new RecordTriageRequest(TriagePriority.Urgent), None));
        }

        [Fact]
        public async Task LatestTriageIsTheMostRecentlyPerformed()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Nurse);
            var encounter = harness.SeedEncounter();
            harness.Time.Now = ClinicalHarness.Start.AddMinutes(30);

            await harness.Records.RecordTriageAsync(encounter.Id, new RecordTriageRequest(TriagePriority.Standard, PerformedAtUtc: ClinicalHarness.Start.AddMinutes(20)), None);
            await harness.Records.RecordTriageAsync(encounter.Id, new RecordTriageRequest(TriagePriority.Immediate, PerformedAtUtc: ClinicalHarness.Start.AddMinutes(5)), None);

            Assert.Equal(TriagePriority.Standard, (await harness.Encounters.GetAsync(encounter.Id, None)).LatestTriagePriority);
            Assert.Equal(TriagePriority.Standard, (await harness.Records.ListTriagesAsync(encounter.Id, None))[0].Priority);
        }
    }
}
