using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Tests.Patients
{
    public class PatientTests
    {
        private static Patient NewPatient() => Patient.Register("Ana María", "Pérez Gómez", new DateOnly(1990, 5, 1), BiologicalSex.Female);

        [Fact]
        public void PatientExistsWithoutAnAccount()
        {
            var patient = NewPatient();

            Assert.NotEqual(Guid.Empty, patient.Id);
            Assert.Null(patient.IdentityUserId);
            Assert.Equal(PatientIdentityStatus.Unverified, patient.IdentityStatus);
        }

        [Fact]
        public void AccountLinkIsOptionalAndCannotBeSilentlyReplaced()
        {
            var patient = NewPatient();
            var account = Guid.NewGuid();

            patient.LinkIdentityUser(account);
            patient.LinkIdentityUser(account);

            Assert.Equal(account, patient.IdentityUserId);
            Assert.Throws<DomainException>(() => patient.LinkIdentityUser(Guid.NewGuid()));

            patient.UnlinkIdentityUser();
            Assert.Null(patient.IdentityUserId);
        }

        [Fact]
        public void FutureDateOfBirthIsRejected()
        {
            var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

            Assert.Throws<ArgumentOutOfRangeException>(() => Patient.Register("Ana", "Pérez", tomorrow, BiologicalSex.Female));
        }

        [Fact]
        public void BlankNamesAreRejected()
        {
            Assert.Throws<ArgumentException>(() => Patient.Register(" ", "Pérez", null, BiologicalSex.Unknown));
        }
    }
}
