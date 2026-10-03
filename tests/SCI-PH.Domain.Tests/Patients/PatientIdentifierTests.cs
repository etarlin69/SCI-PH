using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Tests.Patients
{
    public class PatientIdentifierTests
    {
        private static Patient NewPatient() => Patient.Register("Luis", "Martínez", null, BiologicalSex.Male);

        [Theory]
        [InlineData(PatientIdentifierType.NationalId, " 001-1234567-8 ", "00112345678")]
        [InlineData(PatientIdentifierType.Passport, "rd 12.345", "RD12345")]
        [InlineData(PatientIdentifierType.InternalCode, "hc-0042", "HC0042")]
        [InlineData(PatientIdentifierType.Phone, "+1 (809) 555-1234", "+18095551234")]
        [InlineData(PatientIdentifierType.Email, " Ana@Example.COM ", "ana@example.com")]
        [InlineData(PatientIdentifierType.QrCode, "  qR-AbC-123 ", "qR-AbC-123")]
        public void ValuesAreNormalizedByType(PatientIdentifierType type, string value, string expected)
        {
            Assert.Equal(expected, PatientIdentifier.Normalize(type, value));
        }

        [Fact]
        public void IdentifierKeepsOriginalValueSourceAndNormalizedIssuer()
        {
            var identifier = NewPatient().AddIdentifier(
                PatientIdentifierType.NationalId, "001-1234567-8", PatientIdentifierSource.Ocr, issuer: " jce ");

            Assert.Equal("001-1234567-8", identifier.Value);
            Assert.Equal("00112345678", identifier.NormalizedValue);
            Assert.Equal("JCE", identifier.Issuer);
            Assert.Equal(PatientIdentifierSource.Ocr, identifier.Source);
            Assert.Equal(PatientIdentifierStatus.Active, identifier.Status);
            Assert.True(identifier.HasUniqueValue);
        }

        [Fact]
        public void PhoneIsNotAnIdentityBearingValue()
        {
            var phone = NewPatient().AddIdentifier(PatientIdentifierType.Phone, "8095551234", PatientIdentifierSource.Manual);

            Assert.False(phone.HasUniqueValue);
            Assert.DoesNotContain(PatientIdentifierType.Phone, PatientIdentifier.UniqueTypes);
            Assert.Contains(PatientIdentifierType.QrCode, PatientIdentifier.UniqueTypes);
        }

        [Fact]
        public void SameActiveValueCannotBeAddedTwiceToAPatient()
        {
            var patient = NewPatient();
            patient.AddIdentifier(PatientIdentifierType.QrCode, "QR-123", PatientIdentifierSource.QrScan);

            Assert.Throws<InvalidPatientIdentifierException>(() =>
                patient.AddIdentifier(PatientIdentifierType.QrCode, " QR-123 ", PatientIdentifierSource.Manual));
        }

        [Fact]
        public void NewPrimaryDemotesThePreviousPrimaryOfTheSameType()
        {
            var patient = NewPatient();
            var first = patient.AddIdentifier(PatientIdentifierType.Phone, "8095551111", PatientIdentifierSource.Manual, isPrimary: true);
            var second = patient.AddIdentifier(PatientIdentifierType.Phone, "8095552222", PatientIdentifierSource.Manual, isPrimary: true);
            var nationalId = patient.AddIdentifier(PatientIdentifierType.NationalId, "00112345678", PatientIdentifierSource.Manual, isPrimary: true);

            Assert.False(first.IsPrimary);
            Assert.True(second.IsPrimary);
            Assert.True(nationalId.IsPrimary);

            patient.MakePrimaryIdentifier(first.Id);
            Assert.True(first.IsPrimary);
            Assert.False(second.IsPrimary);
        }

        [Fact]
        public void RevokedIdentifierIsInactiveNotPrimaryAndCanBeReissued()
        {
            var patient = NewPatient();
            var qr = patient.AddIdentifier(PatientIdentifierType.QrCode, "QR-123", PatientIdentifierSource.System, isPrimary: true);

            patient.RevokeIdentifier(qr.Id, DateTimeOffset.UtcNow.AddMinutes(1));

            Assert.Equal(PatientIdentifierStatus.Revoked, qr.Status);
            Assert.NotNull(qr.RevokedAtUtc);
            Assert.False(qr.IsPrimary);
            Assert.False(qr.IsValidAt(DateTimeOffset.UtcNow));
            Assert.Throws<InvalidPatientIdentifierException>(() => patient.VerifyIdentifier(qr.Id, DateTimeOffset.UtcNow.AddMinutes(2)));

            var reissued = patient.AddIdentifier(PatientIdentifierType.QrCode, "QR-123", PatientIdentifierSource.System);
            Assert.True(reissued.IsActive);
        }

        [Fact]
        public void VerificationRecordsTheDate()
        {
            var patient = NewPatient();
            var identifier = patient.AddIdentifier(PatientIdentifierType.NationalId, "00112345678", PatientIdentifierSource.Manual);
            var verifiedAt = DateTimeOffset.UtcNow.AddMinutes(5);

            patient.VerifyIdentifier(identifier.Id, verifiedAt);

            Assert.True(identifier.IsVerified);
            Assert.Equal(verifiedAt, identifier.VerifiedAtUtc);
        }

        [Fact]
        public void ExpiredOrInvalidIdentifiersAreRejected()
        {
            var patient = NewPatient();

            Assert.Throws<InvalidPatientIdentifierException>(() => patient.AddIdentifier(
                PatientIdentifierType.QrCode, "QR-1", PatientIdentifierSource.QrScan, validUntilUtc: DateTimeOffset.UtcNow.AddDays(-1)));
            Assert.Throws<InvalidPatientIdentifierException>(() =>
                patient.AddIdentifier(PatientIdentifierType.Phone, "555", PatientIdentifierSource.Manual));
            Assert.Throws<InvalidPatientIdentifierException>(() =>
                patient.AddIdentifier(PatientIdentifierType.NationalId, "--", PatientIdentifierSource.Manual));
        }
    }
}
