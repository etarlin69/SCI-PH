using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Tests.Medications
{
    public class MedicationTests
    {
        private static readonly DateTimeOffset At = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public void PrescriptionDerivesThePatientFromItsEncounter()
        {
            Assert.DoesNotContain(typeof(Prescription).GetProperties(), property => property.Name == "PatientId");
        }

        [Fact]
        public void PrescriptionIsIssuedOnlyWithDetailsAndThenLocked()
        {
            var prescription = Prescription.Draft(Guid.NewGuid(), Guid.NewGuid());
            Assert.Throws<DomainException>(() => prescription.Issue(At));

            prescription.AddDetail(Guid.NewGuid(), 500m, "mg", MedicationRoute.Oral, "cada 8 horas", durationDays: 7);
            prescription.Issue(At, At.AddDays(30));

            Assert.Equal(PrescriptionStatus.Issued, prescription.Status);
            Assert.Throws<DomainException>(() =>
                prescription.AddDetail(Guid.NewGuid(), 1m, "tab", MedicationRoute.Oral, "cada 24 horas"));
        }

        [Fact]
        public void InvalidDetailsAreRejected()
        {
            var prescription = Prescription.Draft(Guid.NewGuid(), Guid.NewGuid());

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                prescription.AddDetail(Guid.NewGuid(), 0m, "mg", MedicationRoute.Oral, "cada 8 horas"));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                prescription.AddDetail(Guid.NewGuid(), 1m, "mg", MedicationRoute.Oral, "cada 8 horas", durationDays: 0));
        }

        [Fact]
        public void AdministrationRecordsARealDoseAndIsNeverDeleted()
        {
            var prescriptionId = Guid.NewGuid();
            var administration = MedicationAdministration.Record(
                Guid.NewGuid(), Guid.NewGuid(), 4m, "mg", MedicationRoute.Intravenous, At, Guid.NewGuid(), prescriptionId, Guid.NewGuid());

            Assert.Equal(MedicationAdministrationStatus.Completed, administration.Status);
            Assert.Equal(prescriptionId, administration.PrescriptionId);

            administration.MarkEnteredInError("Registrada en el paciente equivocado");
            Assert.Equal(MedicationAdministrationStatus.EnteredInError, administration.Status);
        }

        [Fact]
        public void AdministrationValidatesDoseAndPrescriptionLink()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MedicationAdministration.Record(
                Guid.NewGuid(), Guid.NewGuid(), 0m, "mg", MedicationRoute.Oral, At, Guid.NewGuid()));
            Assert.Throws<ArgumentException>(() => MedicationAdministration.Record(
                Guid.NewGuid(), Guid.NewGuid(), 1m, "mg", MedicationRoute.Oral, At, Guid.NewGuid(), prescriptionDetailId: Guid.NewGuid()));
        }

        [Fact]
        public void CatalogEntryIsSeparateFromAdministration()
        {
            var medication = Medication.Register("Paracetamol", "Tableta", "500 mg", "n02be01", MedicationRoute.Oral);

            Assert.Equal("N02BE01", medication.Code);
            Assert.Equal(MedicationStatus.Active, medication.Status);
            Assert.DoesNotContain(typeof(Medication).GetProperties(), property => property.Name.Contains("Encounter"));
        }
    }
}
