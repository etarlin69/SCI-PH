using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Domain.Tests.Patients
{
    public class MedicalHistoryTests
    {
        [Fact]
        public void HistoryVersionHoldsAllStructuredSections()
        {
            var history = PatientMedicalHistory.Record(Guid.NewGuid(), version: 1);

            history.AddAllergy("Penicilina", AllergySeverity.Severe, ClinicalRecordStatus.Active, reaction: "Anafilaxia");
            history.AddChronicCondition("Hipertensión arterial", ClinicalRecordStatus.Active, "icd-10", "i10");
            history.AddHomeMedication("Losartán 50 mg", HomeMedicationStatus.Active, frequency: "cada 24 horas", route: MedicationRoute.Oral);
            history.AddSurgicalHistory("Apendicectomía", new DateOnly(2010, 3, 2));

            Assert.Single(history.Allergies);
            Assert.Equal(("ICD-10", "I10"), (history.ChronicConditions.Single().CodeSystem, history.ChronicConditions.Single().Code));
            Assert.Single(history.HomeMedications);
            Assert.Single(history.SurgicalHistories);
        }

        [Fact]
        public void InvalidVersionAndDatesAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PatientMedicalHistory.Record(Guid.NewGuid(), version: 0));

            var history = PatientMedicalHistory.Record(Guid.NewGuid(), version: 2);
            Assert.Throws<ArgumentOutOfRangeException>(() => history.AddHomeMedication(
                "Metformina", HomeMedicationStatus.Stopped, startedOn: new DateOnly(2024, 1, 2), stoppedOn: new DateOnly(2024, 1, 1)));
            Assert.Throws<ArgumentException>(() => history.AddChronicCondition("Asma", ClinicalRecordStatus.Active, code: "J45"));
        }

        [Fact]
        public void HomeMedicationIsHistoryNotAnAdministeredDose()
        {
            var homeProperties = typeof(HomeMedication).GetProperties().Select(p => p.Name).ToHashSet();
            var administrationProperties = typeof(MedicationAdministration).GetProperties().Select(p => p.Name).ToHashSet();

            Assert.Contains(nameof(HomeMedication.MedicalHistoryId), homeProperties);
            Assert.DoesNotContain("ClinicalEncounterId", homeProperties);
            Assert.Contains(nameof(MedicationAdministration.ClinicalEncounterId), administrationProperties);
            Assert.Contains(nameof(MedicationAdministration.AdministeredAtUtc), administrationProperties);
            Assert.DoesNotContain("MedicalHistoryId", administrationProperties);
        }
    }
}
