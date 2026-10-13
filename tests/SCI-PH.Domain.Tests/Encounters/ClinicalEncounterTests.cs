using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Exceptions;
using SCI_PH.Domain.ValueObjects;

namespace SCI_PH.Domain.Tests.Encounters
{
    public class ClinicalEncounterTests
    {
        private static readonly DateTimeOffset Start = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly Guid ClinicId = Guid.NewGuid();

        [Fact]
        public void KnownPatientEncounterHasNoTemporaryCode()
        {
            var patientId = Guid.NewGuid();

            var encounter = ClinicalEncounter.CreateForPatient(patientId, ClinicalEncounterType.Consultation, Start, ClinicId, reason: "Control");

            Assert.Equal(patientId, encounter.PatientId);
            Assert.Null(encounter.TemporaryPatientCode);
            Assert.False(encounter.StartedAsUnidentified);
            Assert.Equal(ClinicalEncounterStatus.Created, encounter.Status);
            Assert.Null(encounter.IdentifiedAtUtc);
        }

        [Fact]
        public void UnidentifiedEncounterHasUniqueTemporaryCodeAndNoPatient()
        {
            var first = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Prehospital, Start);
            var second = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Emergency, Start, ClinicId);

            Assert.Null(first.PatientId);
            Assert.Null(first.ClinicId);
            Assert.NotNull(first.TemporaryPatientCode);
            Assert.StartsWith(TemporaryPatientCode.Prefix, first.TemporaryPatientCode!.Value);
            Assert.NotEqual(first.TemporaryPatientCode, second.TemporaryPatientCode);
        }

        [Theory]
        [InlineData(ClinicalEncounterType.Consultation)]
        [InlineData(ClinicalEncounterType.Hospitalization)]
        public void OnlyEmergencyAndPrehospitalCareCanStartAsNn(ClinicalEncounterType type)
        {
            Assert.Throws<InvalidClinicalEncounterStateException>(() => ClinicalEncounter.CreateUnidentified(type, Start, ClinicId));
        }

        [Fact]
        public void NonPrehospitalCareRequiresAClinic()
        {
            Assert.Throws<InvalidClinicalEncounterStateException>(() =>
                ClinicalEncounter.CreateForPatient(Guid.NewGuid(), ClinicalEncounterType.Emergency, Start));
        }

        [Fact]
        public void IdentifyPatientLinksTheSameEncounterAndKeepsTheCode()
        {
            var encounter = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Emergency, Start, ClinicId);
            var originalId = encounter.Id;
            var code = encounter.TemporaryPatientCode;
            var patientId = Guid.NewGuid();
            var staffId = Guid.NewGuid();

            encounter.IdentifyPatient(patientId, staffId, Start.AddMinutes(30));

            Assert.Equal(originalId, encounter.Id);
            Assert.Equal(code, encounter.TemporaryPatientCode);
            Assert.Equal(patientId, encounter.PatientId);
            Assert.Equal(staffId, encounter.IdentifiedByStaffId);
            Assert.Equal(Start.AddMinutes(30), encounter.IdentifiedAtUtc);
            Assert.True(encounter.StartedAsUnidentified);
        }

        [Fact]
        public void IdentifiedEncounterCannotBeReidentified()
        {
            var known = ClinicalEncounter.CreateForPatient(Guid.NewGuid(), ClinicalEncounterType.Emergency, Start, ClinicId);
            var nn = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Emergency, Start, ClinicId);
            var patientId = Guid.NewGuid();
            nn.IdentifyPatient(patientId, Guid.NewGuid(), Start);

            Assert.Throws<PatientAlreadyIdentifiedException>(() => known.IdentifyPatient(Guid.NewGuid(), Guid.NewGuid(), Start));
            Assert.Throws<PatientAlreadyIdentifiedException>(() => nn.IdentifyPatient(Guid.NewGuid(), Guid.NewGuid(), Start));
            Assert.Equal(patientId, nn.PatientId);
        }

        [Fact]
        public void InvalidIdentificationDoesNotPartiallyChangeTheEncounter()
        {
            var encounter = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Prehospital, Start);

            Assert.Throws<ArgumentException>(() => encounter.IdentifyPatient(Guid.Empty, Guid.NewGuid(), Start));
            Assert.Throws<ArgumentOutOfRangeException>(() => encounter.IdentifyPatient(Guid.NewGuid(), Guid.NewGuid(), Start.AddSeconds(-1)));
            Assert.Null(encounter.PatientId);
            Assert.Null(encounter.IdentifiedAtUtc);
            Assert.Null(encounter.IdentifiedByStaffId);
        }

        [Fact]
        public void CancelledEncounterCannotBeIdentified()
        {
            var encounter = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Prehospital, Start);
            encounter.ChangeStatus(ClinicalEncounterStatus.Cancelled, Start.AddMinutes(1));

            Assert.Throws<InvalidClinicalEncounterStateException>(() => encounter.IdentifyPatient(Guid.NewGuid(), Guid.NewGuid(), Start.AddMinutes(2)));
        }

        [Fact]
        public void StatusFollowsTheClinicalFlowAndClosingSetsTheEnd()
        {
            var encounter = ClinicalEncounter.CreateForPatient(Guid.NewGuid(), ClinicalEncounterType.Emergency, Start, ClinicId);

            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, Start.AddMinutes(1));
            encounter.ChangeStatus(ClinicalEncounterStatus.Transferred, Start.AddMinutes(10));
            encounter.ChangeStatus(ClinicalEncounterStatus.Received, Start.AddMinutes(40));
            encounter.ChangeStatus(ClinicalEncounterStatus.InEvolution, Start.AddHours(1));
            Assert.Null(encounter.EndedAtUtc);

            encounter.ChangeStatus(ClinicalEncounterStatus.Closed, Start.AddHours(5));

            Assert.Equal(ClinicalEncounterStatus.Closed, encounter.Status);
            Assert.Equal(Start.AddHours(5), encounter.EndedAtUtc);
            Assert.NotNull(encounter.UpdatedAtUtc);
        }

        [Theory]
        [InlineData(ClinicalEncounterStatus.Received)]
        [InlineData(ClinicalEncounterStatus.Closed)]
        [InlineData(ClinicalEncounterStatus.Created)]
        public void InvalidTransitionsFromCreatedAreRejected(ClinicalEncounterStatus target)
        {
            var encounter = ClinicalEncounter.CreateForPatient(Guid.NewGuid(), ClinicalEncounterType.Consultation, Start, ClinicId);

            Assert.Throws<InvalidClinicalEncounterStateException>(() => encounter.ChangeStatus(target, Start.AddMinutes(1)));
            Assert.Equal(ClinicalEncounterStatus.Created, encounter.Status);
        }

        [Fact]
        public void ClosedEncounterIsFinal()
        {
            var encounter = ClinicalEncounter.CreateForPatient(Guid.NewGuid(), ClinicalEncounterType.Consultation, Start, ClinicId);
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, Start);
            encounter.ChangeStatus(ClinicalEncounterStatus.Closed, Start.AddHours(1));

            Assert.Throws<InvalidClinicalEncounterStateException>(() => encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, Start.AddHours(2)));
        }

        [Fact]
        public void ReceivingATransferredEncounterMovesItToTheDestinationClinic()
        {
            var destination = Guid.NewGuid();
            var encounter = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Emergency, Start, ClinicId);
            var code = encounter.TemporaryPatientCode;
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, Start);
            encounter.ChangeStatus(ClinicalEncounterStatus.Transferred, Start.AddMinutes(10));

            encounter.ReceiveAtClinic(destination, Start.AddMinutes(40));

            Assert.Equal(ClinicalEncounterStatus.Received, encounter.Status);
            Assert.Equal(destination, encounter.ClinicId);
            Assert.Equal(code, encounter.TemporaryPatientCode);
            Assert.Null(encounter.EndedAtUtc);
            Assert.NotNull(encounter.UpdatedAtUtc);
            // The same encounter continues at the destination with the existing transitions.
            Assert.True(encounter.CanChangeStatusTo(ClinicalEncounterStatus.Closed));
        }

        [Fact]
        public void PrehospitalCareWithoutClinicGetsTheDestinationClinicOnArrival()
        {
            var destination = Guid.NewGuid();
            var encounter = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Prehospital, Start);
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, Start);
            encounter.ChangeStatus(ClinicalEncounterStatus.Transferred, Start.AddMinutes(5));

            encounter.ReceiveAtClinic(destination, Start.AddMinutes(25));

            Assert.Equal(destination, encounter.ClinicId);
            Assert.Equal(ClinicalEncounterStatus.Received, encounter.Status);
        }

        [Theory]
        [InlineData(ClinicalEncounterStatus.Created)]
        [InlineData(ClinicalEncounterStatus.InProgress)]
        [InlineData(ClinicalEncounterStatus.Closed)]
        public void OnlyATransferredEncounterCanBeReceivedAndARejectedReceptionChangesNothing(ClinicalEncounterStatus status)
        {
            var encounter = ClinicalEncounter.CreateForPatient(Guid.NewGuid(), ClinicalEncounterType.Emergency, Start, ClinicId);
            if (status != ClinicalEncounterStatus.Created)
            {
                encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, Start);
            }

            if (status == ClinicalEncounterStatus.Closed)
            {
                encounter.ChangeStatus(ClinicalEncounterStatus.Closed, Start.AddMinutes(5));
            }

            Assert.Throws<InvalidClinicalEncounterStateException>(() => encounter.ReceiveAtClinic(Guid.NewGuid(), Start.AddMinutes(10)));
            Assert.Equal(status, encounter.Status);
            Assert.Equal(ClinicId, encounter.ClinicId);
        }

        [Fact]
        public void ReceptionRequiresADestinationAndATimeWithinTheEncounter()
        {
            var encounter = ClinicalEncounter.CreateForPatient(Guid.NewGuid(), ClinicalEncounterType.Emergency, Start, ClinicId);
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, Start);
            encounter.ChangeStatus(ClinicalEncounterStatus.Transferred, Start.AddMinutes(5));

            Assert.Throws<ArgumentException>(() => encounter.ReceiveAtClinic(Guid.Empty, Start.AddMinutes(10)));
            Assert.ThrowsAny<ArgumentException>(() => encounter.ReceiveAtClinic(Guid.NewGuid(), Start.AddMinutes(-1)));
            Assert.Equal(ClinicalEncounterStatus.Transferred, encounter.Status);
            Assert.Equal(ClinicId, encounter.ClinicId);
        }
    }
}
