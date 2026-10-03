using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Tests.Patients
{
    public class IdentificationAttemptTests
    {
        private static readonly IdentificationAttemptContext Staff = new(PerformedByStaffId: Guid.NewGuid());
        private static readonly IdentificationAttemptContext Device = new(DeviceReference: "ESP32-R307-01", Provider: "R307");

        [Theory]
        [InlineData(IdentificationMethod.QrCode)]
        [InlineData(IdentificationMethod.NationalId)]
        [InlineData(IdentificationMethod.Passport)]
        [InlineData(IdentificationMethod.Phone)]
        public void IdentifierMethodsCanReferenceTheMatchedIdentifier(IdentificationMethod method)
        {
            var patientId = Guid.NewGuid();
            var identifierId = Guid.NewGuid();

            var attempt = IdentificationAttempt.Succeeded(method, patientId, DateTimeOffset.UtcNow, Staff, patientIdentifierId: identifierId);

            Assert.Equal(IdentificationOutcome.Success, attempt.Outcome);
            Assert.Equal(patientId, attempt.PatientId);
            Assert.Equal(identifierId, attempt.PatientIdentifierId);
            Assert.Null(attempt.FailureReason);
        }

        [Theory]
        [InlineData(IdentificationMethod.Face)]
        [InlineData(IdentificationMethod.Fingerprint)]
        public void BiometricMethodsCanReferenceTheMatchedEnrollment(IdentificationMethod method)
        {
            var referenceId = Guid.NewGuid();

            var attempt = IdentificationAttempt.Succeeded(method, Guid.NewGuid(), DateTimeOffset.UtcNow, Device, biometricReferenceId: referenceId);

            Assert.Equal(referenceId, attempt.BiometricReferenceId);
            Assert.Equal("ESP32-R307-01", attempt.DeviceReference);
            Assert.Null(attempt.ActorIdentityUserId);
        }

        [Fact]
        public void EvidenceMustMatchTheMethod()
        {
            Assert.Throws<DomainException>(() => IdentificationAttempt.Succeeded(
                IdentificationMethod.QrCode, Guid.NewGuid(), DateTimeOffset.UtcNow, Staff, biometricReferenceId: Guid.NewGuid()));
            Assert.Throws<DomainException>(() => IdentificationAttempt.Succeeded(
                IdentificationMethod.Fingerprint, Guid.NewGuid(), DateTimeOffset.UtcNow, Device, patientIdentifierId: Guid.NewGuid()));
        }

        [Fact]
        public void FailureHasReasonAndNoPatient()
        {
            var attempt = IdentificationAttempt.Failed(
                IdentificationMethod.Fingerprint, IdentificationFailureReason.NoMatch, DateTimeOffset.UtcNow, Device);

            Assert.Equal(IdentificationOutcome.Failure, attempt.Outcome);
            Assert.Equal(IdentificationFailureReason.NoMatch, attempt.FailureReason);
            Assert.Null(attempt.PatientId);
        }

        [Fact]
        public void AnActorIsRequired()
        {
            Assert.Throws<DomainException>(() => IdentificationAttempt.Failed(
                IdentificationMethod.Manual, IdentificationFailureReason.Other, DateTimeOffset.UtcNow, new IdentificationAttemptContext()));
        }

        [Fact]
        public void AttemptHasNoPropertyForBiometricCaptures()
        {
            Assert.DoesNotContain(typeof(IdentificationAttempt).GetProperties(), property => property.PropertyType == typeof(byte[]));
            Assert.DoesNotContain(typeof(PatientBiometricReference).GetProperties(), property => property.PropertyType == typeof(byte[]));
        }
    }
}
