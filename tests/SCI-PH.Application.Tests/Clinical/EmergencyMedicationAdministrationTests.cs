using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Tests.Clinical
{
    // A dose already given on a prehospital emergency (PrehospitalEmergencyService.RecordMedicationAdministrationAsync):
    // an active catalog medication, no prescription, dose > 0 with at most 3 decimals and below 10^9, a free unit of 1 to
    // 24 characters, a MedicationRoute, one audit entry, idempotent, administeredAt from the start of the emergency up to
    // 5 minutes after the server's clock. The Web path keeps the clinic rule.
    public class EmergencyMedicationAdministrationTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private static readonly DateTimeOffset StartedAt = ClinicalHarness.Start.AddMinutes(-10);

        private static RecordEmergencyMedicationAdministrationRequest Dose(
            Medication medication, DateTimeOffset? administeredAt = null, Guid? clientRequestId = null, decimal dose = 500m,
            string doseUnit = "mg", MedicationRoute route = MedicationRoute.Oral, string? notes = "Con agua") =>
            new(clientRequestId ?? Guid.NewGuid(), medication.Id, dose, doseUnit, route, administeredAt ?? ClinicalHarness.Start, notes);

        private static async Task<(ClinicalHarness Harness, ClinicalEncounter Emergency, Medication Medication)> OpenedEmergencyAsync(
            DateTimeOffset? startedAt = null)
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var opened = await harness.Emergencies.OpenAsync(
                new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), startedAt ?? StartedAt, "Dolor"), None);
            return (harness, harness.Store.Encounters.Single(e => e.Id == opened.Emergency.Id), harness.SeedMedication());
        }

        private static List<MedicationAdministration> Administrations(ClinicalHarness harness) => harness.Store.Administrations;

        private static int AdministrationAudits(ClinicalHarness harness) =>
            harness.Store.Audit.Count(a => a.Action == AuditActionType.Create && a.EntityName == nameof(MedicationAdministration));

        [Fact]
        public async Task ADoseIsRecordedWithoutPrescriptionOnTheNnEmergencyWithItsAuditAndReceipt()
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync();
            var request = Dose(medication, ClinicalHarness.Start.AddMinutes(-2), route: MedicationRoute.Intravenous);

            var result = await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, request, None);

            Assert.True(result.Created);
            var staffId = (await harness.Actor.GetAsync(None)).StaffId;
            var row = Assert.Single(Administrations(harness));
            Assert.Equal((emergency.Id, medication.Id, 500m, "mg", MedicationRoute.Intravenous, request.AdministeredAt, staffId),
                (row.ClinicalEncounterId, row.MedicationId, row.Dose, row.DoseUnit, row.Route, row.AdministeredAtUtc, row.AdministeredByStaffId));
            Assert.Equal(((Guid?)null, (Guid?)null, MedicationAdministrationStatus.Completed, "Con agua"),
                (row.PrescriptionId, row.PrescriptionDetailId, row.Status, row.Notes));

            var audit = Assert.Single(harness.Store.Audit, a => a.EntityName == nameof(MedicationAdministration));
            Assert.Equal((AuditActionType.Create, row.Id.ToString(), emergency.Id, (Guid?)null),
                (audit.Action, audit.EntityId, audit.ClinicalEncounterId, audit.PatientId));
            var receipt = harness.Store.Receipts.Single(r => r.Key.Operation == PrehospitalEmergencyService.RecordMedicationAdministrationOperation);
            Assert.Equal(new IdempotencyKey(harness.User.UserId!.Value, PrehospitalEmergencyService.RecordMedicationAdministrationOperation, request.ClientRequestId), receipt.Key);
            Assert.Equal(row.Id, receipt.ResourceId);
            Assert.Equal(new EmergencyMedicationAdministrationSummary(
                row.Id, emergency.Id, medication.Id, medication.Name, 500m, "mg", MedicationRoute.Intravenous, request.AdministeredAt, "Con agua"),
                result.Administration);
            Assert.Contains(emergency.Id, harness.Store.GuardedEncounters);
        }

        // The default route is a suggestion of the catalog, not a rule: any MedicationRoute is accepted.
        [Theory]
        [MemberData(nameof(Routes))]
        public async Task AnyRouteIsAcceptedWhateverTheDefaultRoute(MedicationRoute route)
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync();

            await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, Dose(medication, route: route), None);

            Assert.Equal(route, Assert.Single(Administrations(harness)).Route);
        }

        public static TheoryData<MedicationRoute> Routes() => new(Enum.GetValues<MedicationRoute>());

        [Fact]
        public async Task AnUndefinedRouteIsRejected()
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync();

            var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordMedicationAdministrationAsync(
                emergency.Id, Dose(medication, route: (MedicationRoute)99), None));

            Assert.Equal("route", error.Field);
            Assert.Empty(Administrations(harness));
        }

        [Fact]
        public async Task TheCatalogListsOnlyActiveMedicationsWithoutStatusAndNeedsClinicalStaff()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var paracetamol = harness.SeedMedication("Paracetamol");
            harness.SeedMedication("Ibuprofeno", active: false);
            var amoxicilina = harness.SeedMedication("Amoxicilina");

            var catalog = await harness.Emergencies.ListMedicationCatalogAsync(None);

            Assert.Equal([amoxicilina.Id, paracetamol.Id], catalog.Select(m => m.Id));
            var item = catalog.Single(m => m.Id == paracetamol.Id);
            Assert.Equal((paracetamol.Code, "Paracetamol", "Tableta", "500 mg", (MedicationRoute?)MedicationRoute.Oral),
                (item.Code, item.Name, item.Presentation, item.Concentration, item.DefaultRoute));
            Assert.DoesNotContain(typeof(PrehospitalMedicationCatalogItem).GetProperties(), p => p.Name == "Status");

            var noStaff = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic), harness.Store);
            await Assert.ThrowsAsync<AccessDeniedException>(() => noStaff.Emergencies.ListMedicationCatalogAsync(None));
        }

        [Theory]
        [InlineData("0.001", true)]
        [InlineData("500", true)]
        [InlineData("2.5", true)]
        [InlineData("0.125", true)]
        [InlineData("999999999.999", true)]
        [InlineData("0", false)]
        [InlineData("-1", false)]
        [InlineData("0.0001", false)]
        [InlineData("1.2345", false)]
        [InlineData("1000000000", false)]
        [InlineData("1000000000.5", false)]
        public async Task TheDoseIsPositiveWithAtMostThreeDecimalsBelowOneBillion(string text, bool accepted)
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync();
            var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

            if (accepted)
            {
                var result = await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, Dose(medication, dose: value), None);
                Assert.Equal(value, result.Administration.Dose);
                Assert.Equal(value, Assert.Single(Administrations(harness)).Dose);
            }
            else
            {
                var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordMedicationAdministrationAsync(
                    emergency.Id, Dose(medication, dose: value), None));
                Assert.Equal("dose", error.Field);
                Assert.Empty(Administrations(harness));
            }
        }

        // The unit is free text (the app's list is only a UI aid): trimmed, kept as written, 1 to 24 characters.
        [Theory]
        [InlineData("mg", "mg")]
        [InlineData("  mL ", "mL")]
        [InlineData("inhalaciones", "inhalaciones")]
        [InlineData("UI", "UI")]
        [InlineData("comprimidos masticables", "comprimidos masticables")]
        public async Task TheDoseUnitIsTrimmedAndKeptAsWritten(string unit, string stored)
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync();

            var result = await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, Dose(medication, doseUnit: unit), None);

            Assert.Equal(stored, result.Administration.DoseUnit);
            Assert.Equal(stored, Assert.Single(Administrations(harness)).DoseUnit);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaa")]   // 25
        public async Task AMissingOrTooLongDoseUnitIsRejected(string? unit)
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync();

            var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordMedicationAdministrationAsync(
                emergency.Id, Dose(medication, doseUnit: unit!), None));

            Assert.Equal("doseUnit", error.Field);
            Assert.Empty(Administrations(harness));
        }

        [Fact]
        public async Task NotesAreOptionalTrimmedAndLimited()
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync();

            await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, Dose(medication, notes: "   "), None);
            await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, Dose(medication, notes: " Sin reacción "), None);
            await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, Dose(medication, notes: new string('a', FieldLengths.LongText)), None);
            var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordMedicationAdministrationAsync(
                emergency.Id, Dose(medication, notes: new string('a', FieldLengths.LongText + 1)), None));

            Assert.Equal("notes", error.Field);
            Assert.Equal([null, "Sin reacción", new string('a', FieldLengths.LongText)], Administrations(harness).Select(a => a.Notes));
        }

        [Fact]
        public async Task AnUnknownMedicationIsNotFoundAndAnInactiveOneIsAConflictWithoutWritingAnything()
        {
            var (harness, emergency, _) = await OpenedEmergencyAsync();
            var inactive = harness.SeedMedication("Retirado", active: false);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Emergencies.RecordMedicationAdministrationAsync(
                emergency.Id, new RecordEmergencyMedicationAdministrationRequest(Guid.NewGuid(), Guid.NewGuid(), 1m, "mg", MedicationRoute.Oral, ClinicalHarness.Start), None));
            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.RecordMedicationAdministrationAsync(
                emergency.Id, Dose(inactive), None));
            var empty = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordMedicationAdministrationAsync(
                emergency.Id, new RecordEmergencyMedicationAdministrationRequest(Guid.NewGuid(), Guid.Empty, 1m, "mg", MedicationRoute.Oral, ClinicalHarness.Start), None));

            Assert.Equal("medicationId", empty.Field);
            Assert.Empty(Administrations(harness));
            Assert.Equal(0, AdministrationAudits(harness));
            Assert.DoesNotContain(harness.Store.Receipts, r => r.Key.Operation == PrehospitalEmergencyService.RecordMedicationAdministrationOperation);
            Assert.Equal(ClinicalEncounterStatus.Created, emergency.Status);
        }

        [Fact]
        public async Task AMissingKeyIsRejected()
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync();

            var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordMedicationAdministrationAsync(
                emergency.Id, Dose(medication, clientRequestId: Guid.Empty), None));

            Assert.Equal("clientRequestId", error.Field);
        }

        [Fact]
        public async Task AnIdentifiedEmergencyRecordsItsPatientInTheAudit()
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync();
            var patient = Patient.Register("Ana", "Pérez", new DateOnly(1980, 1, 1), BiologicalSex.Female);
            patient.AddIdentifier(PatientIdentifierType.NationalId, "001-1234567-8", PatientIdentifierSource.Manual, isPrimary: true);
            harness.Store.Patients.Add(patient);
            await harness.Emergencies.IdentifyByDocumentAsync(emergency.Id,
                new IdentifyEmergencyByDocumentRequest(Guid.NewGuid(), PatientIdentifierType.NationalId, "001-1234567-8"), None);

            await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, Dose(medication), None);

            Assert.Equal(patient.Id, Assert.Single(harness.Store.Audit, a => a.EntityName == nameof(MedicationAdministration)).PatientId);
        }

        [Fact]
        public async Task TheFirstAdministrationStartsTheEmergencyOnce()
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync();
            var request = Dose(medication);

            await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, request, None);
            Assert.Equal(ClinicalEncounterStatus.InProgress, emergency.Status);
            var updatedAt = emergency.UpdatedAtUtc;
            var saves = harness.Store.SaveCount;

            var replay = await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, request, None);
            await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, Dose(medication, dose: 250m), None);

            Assert.False(replay.Created);
            Assert.Equal((ClinicalEncounterStatus.InProgress, updatedAt), (emergency.Status, emergency.UpdatedAtUtc));
            Assert.Equal(saves + 1, harness.Store.SaveCount);
        }

        [Theory]
        [InlineData(-600_000, true)]          // == startedAt
        [InlineData(-600_001, false)]         // before startedAt
        [InlineData(0, true)]                 // == now
        [InlineData(300_000, true)]           // now + 5 min
        [InlineData(300_001, false)]          // now + 5 min + 1 ms
        public async Task AdministeredAtGoesFromTheStartToFiveMinutesAhead(long offsetMilliseconds, bool accepted)
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync();
            var request = Dose(medication, ClinicalHarness.Start.AddMilliseconds(offsetMilliseconds));

            if (accepted)
            {
                await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, request, None);
                Assert.Single(Administrations(harness));
            }
            else
            {
                var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, request, None));
                Assert.Equal("administeredAt", error.Field);
                Assert.Empty(Administrations(harness));
            }
        }

        [Fact]
        public async Task AdministeredAtIsStoredInUtcAndTheToleratedFutureStartIsAccepted()
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync(startedAt: ClinicalHarness.Start.AddMinutes(4));
            var local = ClinicalHarness.Start.AddMinutes(4.5).ToOffset(TimeSpan.FromHours(-4));

            var result = await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, Dose(medication, local), None);

            Assert.Equal(TimeSpan.Zero, result.Administration.AdministeredAtUtc.Offset);
            Assert.Equal(TimeSpan.Zero, Assert.Single(Administrations(harness)).AdministeredAtUtc.Offset);
            Assert.Equal(ClinicalEncounterStatus.InProgress, emergency.Status);
        }

        // Same key + same dose (500 = 500.0, another offset, the unit and notes with other spaces): the same answer, nothing
        // new. Even after the clock moved on and the medication was deactivated: the dose was valid when it was recorded.
        [Fact]
        public async Task RetryingReplaysTheDoseEvenAfterTheMedicationWasDeactivated()
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync();
            var key = Guid.NewGuid();
            var first = await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, Dose(medication, clientRequestId: key), None);
            var audits = harness.Store.Audit.Count;
            harness.Time.Now = ClinicalHarness.Start.AddHours(30);
            medication.Deactivate();

            var replay = await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, Dose(
                medication, ClinicalHarness.Start.ToOffset(TimeSpan.FromHours(-4)), key, 500.0m, " mg ", notes: " Con agua "), None);

            Assert.False(replay.Created);
            Assert.Equal(first.Administration, replay.Administration);
            Assert.Single(Administrations(harness));
            Assert.Equal(audits, harness.Store.Audit.Count);
        }

        [Fact]
        public async Task TheSameKeyWithAnyOtherValueOrEmergencyIsAConflict()
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync();
            var other = await harness.Emergencies.OpenAsync(new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), StartedAt, "Caída"), None);
            var otherMedication = harness.SeedMedication("Metamizol");
            var key = Guid.NewGuid();
            await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, Dose(medication, clientRequestId: key), None);
            var original = Assert.Single(Administrations(harness));

            foreach (var (emergencyId, request) in new[]
            {
                (emergency.Id, Dose(otherMedication, clientRequestId: key)),
                (emergency.Id, Dose(medication, clientRequestId: key, dose: 500.001m)),
                (emergency.Id, Dose(medication, clientRequestId: key, doseUnit: "MG")),
                (emergency.Id, Dose(medication, clientRequestId: key, route: MedicationRoute.Intravenous)),
                (emergency.Id, Dose(medication, ClinicalHarness.Start.AddSeconds(-1), key)),
                (emergency.Id, Dose(medication, clientRequestId: key, notes: null)),
                (other.Emergency.Id, Dose(medication, clientRequestId: key))
            })
            {
                await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.RecordMedicationAdministrationAsync(emergencyId, request, None));
            }

            Assert.Same(original, Assert.Single(Administrations(harness)));
            Assert.Equal((500m, "mg", MedicationRoute.Oral, "Con agua"), (original.Dose, original.DoseUnit, original.Route, original.Notes));
        }

        [Fact]
        public async Task LosingARaceWithTheSameDoseReturnsTheWinner()
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync();
            var request = Dose(medication);
            var key = new IdempotencyKey(harness.User.UserId!.Value, PrehospitalEmergencyService.RecordMedicationAdministrationOperation, request.ClientRequestId);
            var hash = IdempotencyRequestHash.Compute(
                emergency.Id.ToString("D"), medication.Id.ToString("D"), "500", "mg", "Oral", IdempotencyRequestHash.Instant(request.AdministeredAt), "Con agua");
            var winnerId = Guid.NewGuid();
            harness.Store.BeforeSave = () =>
            {
                harness.Store.BeforeSave = null;
                harness.Store.Receipts.Add(new IdempotencyReceipt(key, hash, winnerId, ClinicalHarness.Start));
            };

            var result = await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, request, None);

            Assert.False(result.Created);
            Assert.Equal(winnerId, result.Administration.Id);
            Assert.Empty(Administrations(harness));
            Assert.Equal(0, AdministrationAudits(harness));
        }

        [Fact]
        public async Task AReplayStillChecksAccess()
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync();
            var request = Dose(medication);
            await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, request, None);
            harness.Store.Assignments.Single(a => a.ClinicalEncounterId == emergency.Id).Unassign(ClinicalHarness.Start.AddMinutes(1));

            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, request, None));
        }

        // Another paramedic is denied before the medication is even looked up.
        [Fact]
        public async Task OnlyTheAssignedParamedicRecordsOnThePrehospitalEmergency()
        {
            var (harness, emergency, _) = await OpenedEmergencyAsync();
            var other = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic), harness.Store);
            other.Staff.Add(other.User.UserId!.Value, true, Guid.NewGuid());

            await Assert.ThrowsAsync<AccessDeniedException>(() => other.Emergencies.RecordMedicationAdministrationAsync(emergency.Id,
                new RecordEmergencyMedicationAdministrationRequest(Guid.NewGuid(), Guid.NewGuid(), 1m, "mg", MedicationRoute.Oral, ClinicalHarness.Start), None));
            Assert.Empty(Administrations(harness));
        }

        [Fact]
        public async Task AnUnknownOrNonPrehospitalEncounterIsNotAnEmergency()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var medication = harness.SeedMedication();
            var hospital = harness.SeedEncounter();

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Emergencies.RecordMedicationAdministrationAsync(hospital.Id, Dose(medication), None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Emergencies.RecordMedicationAdministrationAsync(Guid.NewGuid(), Dose(medication), None));
            Assert.Empty(Administrations(harness));
        }

        // Transferred without clinic is the way to the hospital and accepts records (EmergencyTransferTests).
        [Theory]
        [InlineData(ClinicalEncounterStatus.Closed)]
        [InlineData(ClinicalEncounterStatus.Cancelled)]
        public async Task AnEmergencyThatNoLongerAcceptsRecordsRejectsTheDose(ClinicalEncounterStatus status)
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync();
            if (status != ClinicalEncounterStatus.Cancelled)
            {
                emergency.ChangeStatus(ClinicalEncounterStatus.InProgress, ClinicalHarness.Start);
            }

            emergency.ChangeStatus(status, ClinicalHarness.Start);

            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, Dose(medication), None));
            Assert.Empty(Administrations(harness));
            Assert.Equal(status, emergency.Status);
        }

        // Regression: the Web path still needs the clinic, so a clinic-less emergency stays out of it.
        [Fact]
        public async Task TheWebAdministrationKeepsTheClinicRule()
        {
            var (harness, emergency, medication) = await OpenedEmergencyAsync();

            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Medications.RecordAdministrationAsync(emergency.Id,
                new RecordMedicationAdministrationRequest(medication.Id, 1m, "mg", MedicationRoute.Oral, ClinicalHarness.Start), None));
            Assert.Empty(Administrations(harness));
        }
    }
}
