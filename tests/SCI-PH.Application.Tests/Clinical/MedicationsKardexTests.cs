using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Medications;

namespace SCI_PH.Application.Tests.Clinical
{
    public class MedicationsKardexTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        private static (ClinicalHarness Harness, ClinicalEncounter Encounter) InProgress(string role = SecurityRoles.Doctor)
        {
            var harness = ClinicalHarness.ForRole(role);
            var encounter = harness.SeedEncounter(patientId: harness.SeedPatient().Id);
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, ClinicalHarness.Start);
            harness.Time.Now = ClinicalHarness.Start.AddMinutes(30);
            return (harness, encounter);
        }

        private static PrescriptionDetailInput Line(Guid medicationId, decimal dose = 500) =>
            new(medicationId, dose, "mg", MedicationRoute.Oral, "cada 8 horas", 5, "Con alimentos");

        private static CreatePrescriptionRequest Order(params PrescriptionDetailInput[] lines) => new(lines, "Tratamiento de prueba");

        private static RecordMedicationAdministrationRequest Dose(
            Guid medicationId, DateTimeOffset at, Guid? prescriptionId = null, Guid? detailId = null) =>
            new(medicationId, 500, "mg", MedicationRoute.Oral, at, prescriptionId, detailId);

        // A shared store: the doctor prescribes, then the nurse (same clinic) administers.
        private static ClinicalHarness NurseOf(ClinicalHarness doctor)
        {
            var nurse = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Nurse), doctor.Store);
            nurse.Staff.Add(nurse.User.UserId!.Value, true, doctor.ClinicId);
            nurse.Time.Now = doctor.Time.Now;
            return nurse;
        }

        [Fact]
        public async Task TheCatalogListsActiveAndInactiveMedications()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Nurse);
            harness.SeedMedication("Paracetamol");
            harness.SeedMedication("Dipirona", active: false);

            var catalog = await harness.Medications.ListMedicationsAsync(None);

            Assert.Equal(["Dipirona", "Paracetamol"], catalog.Select(m => m.Name));
            Assert.Equal(MedicationStatus.Inactive, catalog[0].Status);
        }

        [Fact]
        public async Task OnlyActiveClinicalStaffReadTheCatalog()
        {
            var harness = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Doctor));

            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Medications.ListMedicationsAsync(None));
        }

        [Fact]
        public async Task AValidPrescriptionIsIssuedWithItsDetailsByTheCurrentStaffInOneSave()
        {
            var (harness, encounter) = InProgress();
            var paracetamol = harness.SeedMedication("Paracetamol");
            var ibuprofen = harness.SeedMedication("Ibuprofeno");
            var saves = harness.Store.SaveCount;

            var prescription = await harness.Medications.CreatePrescriptionAsync(encounter.Id, Order(Line(paracetamol.Id), Line(ibuprofen.Id, 400)), None);

            var stored = Assert.Single(harness.Store.Prescriptions);
            Assert.Equal(saves + 1, harness.Store.SaveCount);
            Assert.Equal(PrescriptionStatus.Issued, stored.Status);
            Assert.Equal(harness.Time.Now, stored.IssuedAtUtc);
            Assert.Equal(2, stored.Details.Count);
            Assert.Equal((await harness.Actor.GetAsync(None)).StaffId, prescription.PrescribedByStaffId);
            Assert.Contains(prescription.Details, d => d.MedicationName == "Ibuprofeno" && d.Dose == 400 && d.Frequency == "cada 8 horas");
            Assert.Equal(nameof(Prescription), Assert.Single(harness.Store.Audit).EntityName);
            Assert.Contains(encounter.Id, harness.Store.GuardedEncounters);
            Assert.Single(await harness.Medications.ListPrescriptionsAsync(encounter.Id, None));
        }

        [Fact]
        public async Task TheFirstPrescriptionStartsACreatedEncounter()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Doctor);
            var encounter = harness.SeedEncounter();

            await harness.Medications.CreatePrescriptionAsync(encounter.Id, Order(Line(harness.SeedMedication().Id)), None);

            Assert.Equal(ClinicalEncounterStatus.InProgress, encounter.Status);
        }

        [Fact]
        public async Task UnknownOrInactiveMedicationsAreRejectedAndNothingIsSaved()
        {
            var (harness, encounter) = InProgress();
            var inactive = harness.SeedMedication("Retirado", active: false);
            var saves = harness.Store.SaveCount;

            var unknown = await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                harness.Medications.CreatePrescriptionAsync(encounter.Id, Order(Line(Guid.NewGuid())), None));
            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                harness.Medications.CreatePrescriptionAsync(encounter.Id, Order(Line(inactive.Id)), None));

            Assert.Equal(nameof(Medication), unknown.Resource);
            Assert.Equal(saves, harness.Store.SaveCount);
        }

        [Fact]
        public async Task InvalidPrescriptionDetailsAreValidationErrorsWithTheirField()
        {
            var (harness, encounter) = InProgress();
            var medication = harness.SeedMedication();

            var empty = await Assert.ThrowsAsync<RequestValidationException>(() =>
                harness.Medications.CreatePrescriptionAsync(encounter.Id, Order(), None));
            var dose = await Assert.ThrowsAsync<RequestValidationException>(() =>
                harness.Medications.CreatePrescriptionAsync(encounter.Id, Order(Line(medication.Id), Line(medication.Id, 0)), None));
            var frequency = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Medications.CreatePrescriptionAsync(
                encounter.Id, Order(Line(medication.Id) with { Frequency = " " }), None));
            var duration = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Medications.CreatePrescriptionAsync(
                encounter.Id, Order(Line(medication.Id) with { DurationDays = 0 }), None));
            var route = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Medications.CreatePrescriptionAsync(
                encounter.Id, Order(Line(medication.Id) with { Route = (MedicationRoute)99 }), None));
            var tooMany = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Medications.CreatePrescriptionAsync(
                encounter.Id, Order(Enumerable.Repeat(Line(medication.Id), MedicationService.MaxDetailsPerPrescription + 1).ToArray()), None));

            Assert.Equal("details", empty.Field);
            Assert.Equal("details[1].dose", dose.Field);
            Assert.Equal("details[0].frequency", frequency.Field);
            Assert.Equal("details[0].durationDays", duration.Field);
            Assert.Equal("details[0].route", route.Field);
            Assert.Equal("details", tooMany.Field);
            Assert.Empty(harness.Store.Prescriptions);
        }

        [Fact]
        public async Task MissingForeignOrClosedEncountersRejectPrescriptions()
        {
            var (harness, encounter) = InProgress();
            var medication = harness.SeedMedication();
            var foreign = harness.SeedEncounter(clinicId: Guid.NewGuid());
            await harness.Encounters.CloseAsync(encounter.Id, new CloseEncounterRequest(), None);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                harness.Medications.CreatePrescriptionAsync(Guid.NewGuid(), Order(Line(medication.Id)), None));
            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                harness.Medications.CreatePrescriptionAsync(foreign.Id, Order(Line(medication.Id)), None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Medications.ListPrescriptionsAsync(foreign.Id, None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Medications.ListAdministrationsAsync(foreign.Id, None));
            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                harness.Medications.CreatePrescriptionAsync(encounter.Id, Order(Line(medication.Id)), None));
            Assert.Empty(harness.Store.Prescriptions);
        }

        [Fact]
        public async Task InvalidActorsCannotPrescribeOrAdminister()
        {
            var noStaff = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Doctor));
            var encounter = noStaff.SeedEncounter(clinicId: Guid.NewGuid());
            var medication = noStaff.SeedMedication();
            var inactive = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Nurse));
            inactive.Staff.Add(inactive.User.UserId!.Value, false, Guid.NewGuid());

            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                noStaff.Medications.CreatePrescriptionAsync(encounter.Id, Order(Line(medication.Id)), None));
            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                noStaff.Medications.RecordAdministrationAsync(encounter.Id, Dose(medication.Id, ClinicalHarness.Start), None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => inactive.Medications.ListMedicationsAsync(None));
        }

        [Fact]
        public async Task ANurseCannotPrescribeForAnIdentifiedPatient()
        {
            var (harness, encounter) = InProgress(SecurityRoles.Nurse);

            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                harness.Medications.CreatePrescriptionAsync(encounter.Id, Order(Line(harness.SeedMedication().Id)), None));
        }

        [Fact]
        public async Task AnAdministrationOfAPrescribedLineIsRecordedByTheCurrentStaff()
        {
            var (doctor, encounter) = InProgress();
            var medication = doctor.SeedMedication("Paracetamol");
            var prescription = await doctor.Medications.CreatePrescriptionAsync(encounter.Id, Order(Line(medication.Id)), None);
            var nurse = NurseOf(doctor);
            var detail = prescription.Details.Single();

            var first = await nurse.Medications.RecordAdministrationAsync(
                encounter.Id, Dose(medication.Id, nurse.Time.Now.AddMinutes(-10), prescription.Id, detail.Id), None);
            var second = await nurse.Medications.RecordAdministrationAsync(
                encounter.Id, Dose(medication.Id, nurse.Time.Now, prescription.Id, detail.Id), None);

            Assert.Equal((await nurse.Actor.GetAsync(None)).StaffId, first.AdministeredByStaffId);
            Assert.Equal(MedicationAdministrationStatus.Completed, first.Status);
            Assert.Equal("Paracetamol", second.MedicationName);
            var kardex = await nurse.Medications.ListAdministrationsAsync(encounter.Id, None);
            Assert.Equal([second.Id, first.Id], kardex.Select(a => a.Id));
            Assert.All(kardex, a => Assert.Equal(detail.Id, a.PrescriptionDetailId));
            Assert.Equal(2, doctor.Store.Audit.Count(a => a.EntityName == nameof(MedicationAdministration) && a.Action == AuditActionType.Create));
        }

        [Fact]
        public async Task AnAdministrationWithoutPrescriptionIsAllowed()
        {
            var (harness, encounter) = InProgress(SecurityRoles.Nurse);
            var medication = harness.SeedMedication();

            var administration = await harness.Medications.RecordAdministrationAsync(encounter.Id, Dose(medication.Id, harness.Time.Now), None);

            Assert.Null(administration.PrescriptionId);
            Assert.Contains(encounter.Id, harness.Store.GuardedEncounters);
        }

        [Fact]
        public async Task AdministrationsMustMatchAPrescriptionOfTheSameEncounter()
        {
            var (doctor, encounter) = InProgress();
            var paracetamol = doctor.SeedMedication("Paracetamol");
            var ibuprofen = doctor.SeedMedication("Ibuprofeno");
            var other = doctor.SeedEncounter(patientId: doctor.SeedPatient().Id);
            other.ChangeStatus(ClinicalEncounterStatus.InProgress, ClinicalHarness.Start);
            var mine = await doctor.Medications.CreatePrescriptionAsync(encounter.Id, Order(Line(paracetamol.Id)), None);
            var theirs = await doctor.Medications.CreatePrescriptionAsync(other.Id, Order(Line(paracetamol.Id)), None);
            var nurse = NurseOf(doctor);
            var now = nurse.Time.Now;
            var records = nurse.Medications;

            await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                records.RecordAdministrationAsync(encounter.Id, Dose(paracetamol.Id, now, theirs.Id), None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                records.RecordAdministrationAsync(encounter.Id, Dose(paracetamol.Id, now, Guid.NewGuid()), None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                records.RecordAdministrationAsync(encounter.Id, Dose(paracetamol.Id, now, mine.Id, theirs.Details.Single().Id), None));
            var wrongDrug = await Assert.ThrowsAsync<RequestValidationException>(() =>
                records.RecordAdministrationAsync(encounter.Id, Dose(ibuprofen.Id, now, mine.Id, mine.Details.Single().Id), None));
            var notPrescribed = await Assert.ThrowsAsync<RequestValidationException>(() =>
                records.RecordAdministrationAsync(encounter.Id, Dose(ibuprofen.Id, now, mine.Id), None));
            var detailWithoutPrescription = await Assert.ThrowsAsync<RequestValidationException>(() =>
                records.RecordAdministrationAsync(encounter.Id, Dose(paracetamol.Id, now, null, mine.Details.Single().Id), None));

            Assert.Equal("medicationId", wrongDrug.Field);
            Assert.Equal("medicationId", notPrescribed.Field);
            Assert.Equal("prescriptionId", detailWithoutPrescription.Field);
            Assert.Empty(doctor.Store.Administrations);
        }

        [Fact]
        public async Task CancelledPrescriptionsCannotBeAdministered()
        {
            var (harness, encounter) = InProgress();
            var medication = harness.SeedMedication();
            var summary = await harness.Medications.CreatePrescriptionAsync(encounter.Id, Order(Line(medication.Id)), None);
            harness.Store.Prescriptions.Single().Cancel(harness.Time.Now);

            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Medications.RecordAdministrationAsync(
                encounter.Id, Dose(medication.Id, harness.Time.Now, summary.Id, summary.Details.Single().Id), None));
        }

        [Fact]
        public async Task InvalidAdministrationValuesAreRejected()
        {
            var (harness, encounter) = InProgress(SecurityRoles.Nurse);
            var medication = harness.SeedMedication();
            var records = harness.Medications;

            var future = await Assert.ThrowsAsync<RequestValidationException>(() =>
                records.RecordAdministrationAsync(encounter.Id, Dose(medication.Id, harness.Time.Now.AddMinutes(1)), None));
            var early = await Assert.ThrowsAsync<RequestValidationException>(() =>
                records.RecordAdministrationAsync(encounter.Id, Dose(medication.Id, ClinicalHarness.Start.AddMinutes(-1)), None));
            var dose = await Assert.ThrowsAsync<RequestValidationException>(() => records.RecordAdministrationAsync(
                encounter.Id, Dose(medication.Id, harness.Time.Now) with { Dose = -1 }, None));
            var unit = await Assert.ThrowsAsync<RequestValidationException>(() => records.RecordAdministrationAsync(
                encounter.Id, Dose(medication.Id, harness.Time.Now) with { DoseUnit = "" }, None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                records.RecordAdministrationAsync(encounter.Id, Dose(Guid.NewGuid(), harness.Time.Now), None));
            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                records.RecordAdministrationAsync(encounter.Id, Dose(harness.SeedMedication("Retirado", active: false).Id, harness.Time.Now), None));

            Assert.Equal("administeredAtUtc", future.Field);
            Assert.Equal("administeredAtUtc", early.Field);
            Assert.Equal("dose", dose.Field);
            Assert.Equal("doseUnit", unit.Field);
            Assert.Empty(harness.Store.Administrations);
        }

        [Fact]
        public async Task NothingIsPrescribedOrAdministeredOnAClosedEncounterButHistoryStaysReadable()
        {
            var (harness, encounter) = InProgress();
            var medication = harness.SeedMedication();
            var prescription = await harness.Medications.CreatePrescriptionAsync(encounter.Id, Order(Line(medication.Id)), None);
            await harness.Medications.RecordAdministrationAsync(encounter.Id, Dose(medication.Id, harness.Time.Now), None);
            await harness.Encounters.CloseAsync(encounter.Id, new CloseEncounterRequest(), None);
            var saves = harness.Store.SaveCount;

            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                harness.Medications.CreatePrescriptionAsync(encounter.Id, Order(Line(medication.Id)), None));
            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Medications.RecordAdministrationAsync(
                encounter.Id, Dose(medication.Id, harness.Time.Now, prescription.Id, prescription.Details.Single().Id), None));

            Assert.Equal(saves, harness.Store.SaveCount);
            Assert.Single(await harness.Medications.ListPrescriptionsAsync(encounter.Id, None));
            Assert.Single(await harness.Medications.ListAdministrationsAsync(encounter.Id, None));
        }

        [Fact]
        public async Task MedicationWritesReCheckTheEncounterConcurrencyTokens()
        {
            var (harness, encounter) = InProgress();
            var medication = harness.SeedMedication();

            await harness.Medications.CreatePrescriptionAsync(encounter.Id, Order(Line(medication.Id)), None);
            Assert.Contains(encounter.Id, harness.Store.GuardedEncounters);
            harness.Store.GuardedEncounters.Clear();
            await harness.Medications.RecordAdministrationAsync(encounter.Id, Dose(medication.Id, harness.Time.Now), None);
            Assert.Contains(encounter.Id, harness.Store.GuardedEncounters);
        }
    }
}
