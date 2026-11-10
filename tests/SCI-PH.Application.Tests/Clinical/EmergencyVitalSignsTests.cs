using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Tests.Clinical
{
    // A reading of vital signs on a prehospital emergency (PrehospitalEmergencyService.RecordVitalSignsAsync): 1 to 7
    // values, all or nothing, one audit entry per measurement, idempotent, recordedAt from the start of the emergency up to
    // 5 minutes after the server's clock. Medications and the Web vital signs keep their own rules.
    public class EmergencyVitalSignsTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private static readonly DateTimeOffset StartedAt = ClinicalHarness.Start.AddMinutes(-10);

        private static RecordEmergencyVitalSignsRequest AllSeven(DateTimeOffset? recordedAt = null, Guid? clientRequestId = null) =>
            new(clientRequestId ?? Guid.NewGuid(), recordedAt ?? ClinicalHarness.Start, HeartRate: 88, RespiratoryRate: 18,
                SystolicPressure: 120, DiastolicPressure: 80, OxygenSaturation: 97, Temperature: 37.5m, Glasgow: 15);

        private static RecordEmergencyVitalSignsRequest HeartRateOnly(int value = 90, DateTimeOffset? recordedAt = null, Guid? clientRequestId = null) =>
            new(clientRequestId ?? Guid.NewGuid(), recordedAt ?? ClinicalHarness.Start, HeartRate: value);

        private static async Task<(ClinicalHarness Harness, ClinicalEncounter Emergency)> OpenedEmergencyAsync(DateTimeOffset? startedAt = null)
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var opened = await harness.Emergencies.OpenAsync(
                new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), startedAt ?? StartedAt, "Dolor torácico"), None);
            return (harness, harness.Store.Encounters.Single(e => e.Id == opened.Emergency.Id));
        }

        private static IEnumerable<VitalSign> Vitals(ClinicalHarness harness) => harness.Store.VitalSigns;

        private static int VitalSignAudits(ClinicalHarness harness) =>
            harness.Store.Audit.Count(a => a.Action == AuditActionType.Create && a.EntityName == nameof(VitalSign));

        [Fact]
        public async Task AllSevenValuesAreRecordedOnTheNnEmergencyWithServerUnitsAndOneAuditEach()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var request = AllSeven(ClinicalHarness.Start.AddMinutes(-2));

            var result = await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, request, None);

            Assert.True(result.Created);
            Assert.Null(emergency.PatientId);
            var staffId = (await harness.Actor.GetAsync(None)).StaffId;
            var rows = Vitals(harness).OrderBy(v => v.Type).ToList();
            Assert.Equal(
            [
                (VitalSignType.HeartRate, 88m, "/min"),
                (VitalSignType.RespiratoryRate, 18m, "/min"),
                (VitalSignType.SystolicBloodPressure, 120m, "mm[Hg]"),
                (VitalSignType.DiastolicBloodPressure, 80m, "mm[Hg]"),
                (VitalSignType.Temperature, 37.5m, "Cel"),
                (VitalSignType.OxygenSaturation, 97m, "%"),
                (VitalSignType.GlasgowComaScale, 15m, "{score}")
            ], rows.Select(v => (v.Type, v.Value, v.Unit)));
            Assert.All(rows, v => Assert.Equal((emergency.Id, request.RecordedAt, staffId, (Guid?)null),
                (v.ClinicalEncounterId, v.MeasuredAtUtc, v.MeasuredByStaffId!.Value, v.TriageId)));

            var audits = harness.Store.Audit.Where(a => a.EntityName == nameof(VitalSign)).ToList();
            Assert.Equal(7, audits.Count);
            Assert.Equal(rows.Select(v => v.Id.ToString()).Order(), audits.Select(a => a.EntityId).Order());
            Assert.All(audits, a => Assert.Equal((AuditActionType.Create, emergency.Id, (Guid?)null), (a.Action, a.ClinicalEncounterId, a.PatientId)));

            var receipt = harness.Store.Receipts.Single(r => r.Key.Operation == PrehospitalEmergencyService.RecordVitalSignsOperation);
            Assert.Equal(new IdempotencyKey(harness.User.UserId!.Value, PrehospitalEmergencyService.RecordVitalSignsOperation, request.ClientRequestId), receipt.Key);
            Assert.Equal(emergency.Id, receipt.ResourceId);

            Assert.Equal((emergency.Id, request.RecordedAt), (result.Reading.EncounterId, result.Reading.RecordedAtUtc));
            Assert.Equal(rows.Select(v => (v.Type, v.Value, v.Unit)), result.Reading.Measurements.Select(m => (m.Type, m.Value, m.Unit)));
        }

        [Fact]
        public async Task OneValueIsEnoughAndOnlyTheSuppliedOnesAreRecorded()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();

            var result = await harness.Emergencies.RecordVitalSignsAsync(emergency.Id,
                new RecordEmergencyVitalSignsRequest(Guid.NewGuid(), ClinicalHarness.Start, HeartRate: 110, OxygenSaturation: 91), None);

            Assert.Equal([VitalSignType.HeartRate, VitalSignType.OxygenSaturation], result.Reading.Measurements.Select(m => m.Type));
            Assert.Equal(2, Vitals(harness).Count());
            Assert.Equal(2, VitalSignAudits(harness));
        }

        [Fact]
        public async Task AnIdentifiedEmergencyRecordsItsPatientInTheAudit()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var patient = Patient.Register("Ana", "Pérez", new DateOnly(1980, 1, 1), BiologicalSex.Female);
            patient.AddIdentifier(PatientIdentifierType.NationalId, "001-1234567-8", PatientIdentifierSource.Manual, isPrimary: true);
            harness.Store.Patients.Add(patient);
            await harness.Documents.ConfirmAsync(emergency.Id,
                new IdentifyEmergencyByDocumentRequest(Guid.NewGuid(), PatientIdentifierType.NationalId, "001-1234567-8"), None);

            await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, HeartRateOnly(), None);

            Assert.Equal(patient.Id, emergency.PatientId);
            var audit = harness.Store.Audit.Single(a => a.EntityName == nameof(VitalSign));
            Assert.Equal(patient.Id, audit.PatientId);
        }

        [Fact]
        public async Task TheFirstReadingStartsTheEmergencyOnce()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            Assert.Equal(ClinicalEncounterStatus.Created, emergency.Status);
            var request = HeartRateOnly();

            await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, request, None);
            Assert.Equal(ClinicalEncounterStatus.InProgress, emergency.Status);
            var saves = harness.Store.SaveCount;

            var replay = await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, request, None);
            await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, HeartRateOnly(95), None);

            Assert.False(replay.Created);
            Assert.Equal(ClinicalEncounterStatus.InProgress, emergency.Status);
            Assert.Equal(saves + 1, harness.Store.SaveCount);
        }

        // Exact plausibility limits (VitalSignLimits), both bounds inclusive; one value out of range rejects the reading.
        [Theory]
        [InlineData("heartRate", 0, 300)]
        [InlineData("respiratoryRate", 0, 100)]
        [InlineData("systolicPressure", 0, 300)]
        [InlineData("diastolicPressure", 0, 250)]
        [InlineData("oxygenSaturation", 0, 100)]
        [InlineData("glasgow", 3, 15)]
        public async Task IntegerValuesAcceptTheirExactLimits(string field, int minimum, int maximum)
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            RecordEmergencyVitalSignsRequest With(int value) => field switch
            {
                "heartRate" => new(Guid.NewGuid(), ClinicalHarness.Start, HeartRate: value),
                "respiratoryRate" => new(Guid.NewGuid(), ClinicalHarness.Start, RespiratoryRate: value),
                "systolicPressure" => new(Guid.NewGuid(), ClinicalHarness.Start, SystolicPressure: value),
                "diastolicPressure" => new(Guid.NewGuid(), ClinicalHarness.Start, DiastolicPressure: value),
                "oxygenSaturation" => new(Guid.NewGuid(), ClinicalHarness.Start, OxygenSaturation: value),
                _ => new(Guid.NewGuid(), ClinicalHarness.Start, Glasgow: value)
            };

            await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, With(minimum), None);
            await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, With(maximum), None);
            var below = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordVitalSignsAsync(emergency.Id, With(minimum - 1), None));
            var above = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordVitalSignsAsync(emergency.Id, With(maximum + 1), None));

            Assert.Equal((field, field), (below.Field, above.Field));
            Assert.Equal(2, Vitals(harness).Count());
        }

        [Theory]
        [InlineData("25", true)]
        [InlineData("25.1", true)]
        [InlineData("25.10", true)]
        [InlineData("37.55", true)]
        [InlineData("45", true)]
        [InlineData("37.500", true)]
        [InlineData("24.99", false)]
        [InlineData("45.01", false)]
        [InlineData("37.555", false)]
        [InlineData("36.001", false)]
        public async Task TemperatureAcceptsUpToTwoDecimalsWithinItsLimits(string text, bool accepted)
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            var request = new RecordEmergencyVitalSignsRequest(Guid.NewGuid(), ClinicalHarness.Start, Temperature: value);

            if (accepted)
            {
                var result = await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, request, None);
                Assert.Equal(value, Assert.Single(result.Reading.Measurements).Value);
                Assert.Equal(value, Assert.Single(Vitals(harness)).Value);
            }
            else
            {
                var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordVitalSignsAsync(emergency.Id, request, None));
                Assert.Equal("temperature", error.Field);
                Assert.Empty(Vitals(harness));
            }
        }

        // All or nothing: one invalid value among seven leaves no measurement, no audit and no receipt.
        [Fact]
        public async Task OneInvalidValueRejectsTheWholeReading()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var audits = harness.Store.Audit.Count;

            var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordVitalSignsAsync(
                emergency.Id, AllSeven() with { Glasgow = 2 }, None));

            Assert.Equal("glasgow", error.Field);
            Assert.Empty(Vitals(harness));
            Assert.Equal(audits, harness.Store.Audit.Count);
            Assert.DoesNotContain(harness.Store.Receipts, r => r.Key.Operation == PrehospitalEmergencyService.RecordVitalSignsOperation);
            Assert.Equal(ClinicalEncounterStatus.Created, emergency.Status);
        }

        [Fact]
        public async Task AnEmptyReadingOrAMissingKeyIsRejected()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();

            await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordVitalSignsAsync(
                emergency.Id, new RecordEmergencyVitalSignsRequest(Guid.NewGuid(), ClinicalHarness.Start), None));
            var key = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordVitalSignsAsync(
                emergency.Id, HeartRateOnly(clientRequestId: Guid.Empty), None));

            Assert.Equal("clientRequestId", key.Field);
            Assert.Empty(Vitals(harness));
        }

        // recordedAt: from the start of the emergency up to now + 5 minutes, both inclusive.
        [Theory]
        [InlineData(-600_000, true)]          // == startedAt
        [InlineData(-600_001, false)]         // before startedAt
        [InlineData(0, true)]                 // == now
        [InlineData(300_000, true)]           // now + 5 min
        [InlineData(300_001, false)]          // now + 5 min + 1 ms
        public async Task RecordedAtGoesFromTheStartToFiveMinutesAhead(long offsetMilliseconds, bool accepted)
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var request = HeartRateOnly(recordedAt: ClinicalHarness.Start.AddMilliseconds(offsetMilliseconds));

            if (accepted)
            {
                await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, request, None);
                Assert.Single(Vitals(harness));
            }
            else
            {
                var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordVitalSignsAsync(emergency.Id, request, None));
                Assert.Equal("recordedAt", error.Field);
                Assert.Empty(Vitals(harness));
            }
        }

        // The device clock 4 minutes ahead: the emergency starts "in the future" and its first reading too. Both accepted.
        [Fact]
        public async Task AReadingRightAfterAnEmergencyStartedInTheToleratedFutureIsAccepted()
        {
            var (harness, emergency) = await OpenedEmergencyAsync(startedAt: ClinicalHarness.Start.AddMinutes(4));

            await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, HeartRateOnly(recordedAt: ClinicalHarness.Start.AddMinutes(4.5)), None);

            Assert.Single(Vitals(harness));
        }

        // A reading that offsets "+00:00" or "-04:00" is the same instant: normalized to UTC.
        [Fact]
        public async Task RecordedAtIsStoredInUtc()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var local = ClinicalHarness.Start.ToOffset(TimeSpan.FromHours(-4));

            var result = await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, HeartRateOnly(recordedAt: local), None);

            Assert.Equal(TimeSpan.Zero, result.Reading.RecordedAtUtc.Offset);
            Assert.Equal(TimeSpan.Zero, Assert.Single(Vitals(harness)).MeasuredAtUtc.Offset);
        }

        // Same key + same reading (37.5 written as 37.50 too): the same answer, nothing new. Even after the clock moved on.
        [Fact]
        public async Task RetryingReplaysTheReadingWithoutNewRowsOrAudits()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var key = Guid.NewGuid();
            var first = await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, AllSeven(clientRequestId: key), None);
            var rows = Vitals(harness).Count();
            var audits = harness.Store.Audit.Count;
            harness.Time.Now = ClinicalHarness.Start.AddHours(30);

            var replay = await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, AllSeven(clientRequestId: key) with { Temperature = 37.50m }, None);

            Assert.False(replay.Created);
            Assert.Equal(first.Reading.EncounterId, replay.Reading.EncounterId);
            Assert.Equal(first.Reading.RecordedAtUtc, replay.Reading.RecordedAtUtc);
            Assert.Equal(first.Reading.Measurements, replay.Reading.Measurements);
            Assert.Equal((rows, audits), (Vitals(harness).Count(), harness.Store.Audit.Count));
        }

        [Fact]
        public async Task TheSameKeyWithAnotherReadingOrEmergencyIsAConflict()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var other = await harness.Emergencies.OpenAsync(new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), StartedAt, "Caída"), None);
            var key = Guid.NewGuid();
            await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, AllSeven(clientRequestId: key), None);
            var rows = Vitals(harness).Count();

            foreach (var (emergencyId, request) in new[]
            {
                (emergency.Id, AllSeven(clientRequestId: key) with { HeartRate = 89 }),
                (emergency.Id, AllSeven(clientRequestId: key) with { Glasgow = null }),
                (emergency.Id, AllSeven(ClinicalHarness.Start.AddSeconds(-1), key)),
                (other.Emergency.Id, AllSeven(clientRequestId: key))
            })
            {
                await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.RecordVitalSignsAsync(emergencyId, request, None));
            }

            Assert.Equal(rows, Vitals(harness).Count());
        }

        // Two readings with different keys at the same instant are two readings: vital signs are a time series.
        [Fact]
        public async Task DifferentKeysAreDifferentReadingsEvenAtTheSameInstant()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();

            await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, HeartRateOnly(), None);
            await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, HeartRateOnly(), None);

            Assert.Equal(2, Vitals(harness).Count());
        }

        // The same reading lost the race to itself: the winner's receipt answers it, nothing is written twice.
        [Fact]
        public async Task LosingARaceWithTheSameReadingReturnsTheWinner()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var request = HeartRateOnly();
            var key = new IdempotencyKey(harness.User.UserId!.Value, PrehospitalEmergencyService.RecordVitalSignsOperation, request.ClientRequestId);
            var hash = IdempotencyRequestHash.Compute(emergency.Id.ToString("D"), IdempotencyRequestHash.Instant(request.RecordedAt), "HeartRate", "90");
            harness.Store.BeforeSave = () =>
            {
                harness.Store.BeforeSave = null;
                harness.Store.Receipts.Add(new IdempotencyReceipt(key, hash, emergency.Id, ClinicalHarness.Start));
            };

            var result = await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, request, None);

            Assert.False(result.Created);
            Assert.Empty(Vitals(harness));
            Assert.Equal(0, VitalSignAudits(harness));
        }

        // A replay still checks access: a receipt is never a way around authorization.
        [Fact]
        public async Task AReplayStillChecksAccess()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var request = HeartRateOnly();
            await harness.Emergencies.RecordVitalSignsAsync(emergency.Id, request, None);
            var assignment = harness.Store.Assignments.Single(a => a.ClinicalEncounterId == emergency.Id);
            assignment.Unassign(ClinicalHarness.Start.AddMinutes(1));

            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Emergencies.RecordVitalSignsAsync(emergency.Id, request, None));
        }

        [Fact]
        public async Task OnlyTheAssignedParamedicRecordsOnThePrehospitalEmergency()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var other = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic), harness.Store);
            other.Staff.Add(other.User.UserId!.Value, true, Guid.NewGuid());

            await Assert.ThrowsAsync<AccessDeniedException>(() => other.Emergencies.RecordVitalSignsAsync(emergency.Id, HeartRateOnly(), None));
            Assert.Empty(Vitals(harness));
        }

        [Fact]
        public async Task AnEncounterThatIsNotPrehospitalIsNotAnEmergency()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var hospital = harness.SeedEncounter();

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Emergencies.RecordVitalSignsAsync(hospital.Id, HeartRateOnly(), None));
        }

        [Fact]
        public async Task AClosedOrTransferredEmergencyRejectsTheReading()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            emergency.ChangeStatus(ClinicalEncounterStatus.Cancelled, ClinicalHarness.Start);

            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.RecordVitalSignsAsync(emergency.Id, HeartRateOnly(), None));
        }

        // Scope protection: the Mobile access rule does not reach medications or the Web vital signs, which still need
        // the clinic (a clinic-less emergency stays out of them).
        [Fact]
        public async Task MedicationsAndTheWebVitalSignsKeepTheClinicRule()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();

            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Medications.RecordAdministrationAsync(emergency.Id,
                new RecordMedicationAdministrationRequest(Guid.NewGuid(), 1m, "mg", MedicationRoute.Oral, ClinicalHarness.Start), None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Records.RecordVitalSignAsync(emergency.Id,
                new RecordVitalSignRequest(VitalSignType.HeartRate, 80, ClinicalHarness.Start), None));
        }
    }
}
