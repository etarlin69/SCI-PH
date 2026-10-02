using SCI_PH.Domain.Entities;

namespace SCI_PH.Domain.Tests
{
    public class ClinicalEncounterTests
    {
        [Fact]
        public void NnCanReceiveCareThenBeIdentifiedWithoutReplacingTheEncounter()
        {
            var encounter = new ClinicalEncounter { Type = ClinicalEncounterType.Emergency };
            var originalId = encounter.Id;
            var temporaryCode = encounter.TemporaryPatientCode;
            var note = new ClinicalNote { ClinicalEncounterId = encounter.Id, Content = "Assessment" };
            var administration = new MedicationAdministration { ClinicalEncounterId = encounter.Id };
            encounter.Notes.Add(note);
            encounter.MedicationAdministrations.Add(administration);
            Assert.Null(encounter.PatientId);
            Assert.StartsWith("NN-", temporaryCode);

            var patientId = Guid.NewGuid();
            var staffId = Guid.NewGuid();
            encounter.IdentifyPatient(patientId, staffId, encounter.StartedAtUtc.AddMinutes(30));

            Assert.Equal(originalId, encounter.Id);
            Assert.Equal(temporaryCode, encounter.TemporaryPatientCode);
            Assert.Equal(patientId, encounter.PatientId);
            Assert.Equal(staffId, encounter.IdentifiedByStaffId);
            Assert.Equal(PatientIdentificationStatus.Identified, encounter.IdentificationStatus);
            Assert.Same(note, Assert.Single(encounter.Notes));
            Assert.Same(administration, Assert.Single(encounter.MedicationAdministrations));
        }

        [Fact]
        public void IdentifiedEncounterCannotBeSilentlyReassigned()
        {
            var encounter = new ClinicalEncounter();
            var patientId = Guid.NewGuid();
            encounter.IdentifyPatient(patientId, Guid.NewGuid(), encounter.StartedAtUtc);
            Assert.Throws<InvalidOperationException>(() =>
                encounter.IdentifyPatient(Guid.NewGuid(), Guid.NewGuid(), encounter.StartedAtUtc));
            Assert.Equal(patientId, encounter.PatientId);
        }

        [Fact]
        public void InvalidIdentificationDoesNotPartiallyMutateEncounter()
        {
            var encounter = new ClinicalEncounter();
            Assert.Throws<ArgumentException>(() =>
                encounter.IdentifyPatient(Guid.Empty, Guid.NewGuid(), encounter.StartedAtUtc));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                encounter.IdentifyPatient(Guid.NewGuid(), Guid.NewGuid(), encounter.StartedAtUtc.AddSeconds(-1)));
            Assert.Null(encounter.PatientId);
            Assert.Null(encounter.IdentifiedAtUtc);
            Assert.Equal(PatientIdentificationStatus.Unidentified, encounter.IdentificationStatus);
        }
    }
}
