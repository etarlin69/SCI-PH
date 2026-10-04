using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Transport;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Tests.Organizations
{
    public class OrganizationTests
    {
        private static readonly DateTimeOffset At = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public void StaffCanLeaveAndReturnToTheSameClinic()
        {
            var clinicId = Guid.NewGuid();
            var staffId = Guid.NewGuid();
            var first = ClinicStaffAffiliation.Start(clinicId, staffId, At);

            first.End(At.AddYears(1));
            var second = ClinicStaffAffiliation.Start(clinicId, staffId, At.AddYears(2));

            Assert.False(first.IsActive);
            Assert.True(second.IsActive);
            Assert.Throws<DomainException>(() => first.End(At.AddYears(3)));
            Assert.Throws<ArgumentOutOfRangeException>(() => second.End(At));
        }

        [Fact]
        public void CodesAreNormalized()
        {
            var clinic = Clinic.Register("Hospital Central", ClinicType.Hospital, "Santo Domingo", "Distrito Nacional", " hc-01 ");
            var staff = ClinicalStaff.Register("Rosa", "Díaz", ClinicalStaffType.Nurse, "exq-123");
            var ambulance = Ambulance.Register(" amb-07 ", clinic.Id);

            Assert.Equal("HC-01", clinic.FacilityCode);
            Assert.Equal(ClinicStatus.Active, clinic.Status);
            Assert.Equal("EXQ-123", staff.ProfessionalLicenseNumber);
            Assert.Equal(StaffStatus.Active, staff.Status);
            Assert.Equal("AMB-07", ambulance.Code);
            Assert.Equal(clinic.Id, ambulance.BaseClinicId);
        }

        [Fact]
        public void StaffIsSeparateFromIdentity()
        {
            var staff = ClinicalStaff.Register("Rosa", "Díaz", ClinicalStaffType.Paramedic);
            var account = Guid.NewGuid();

            Assert.Null(staff.IdentityUserId);
            staff.LinkIdentityUser(account);
            Assert.Throws<DomainException>(() => staff.LinkIdentityUser(Guid.NewGuid()));
        }
    }
}
