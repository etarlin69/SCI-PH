using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Entities.Organizations
{
    // Clinical professional. Login and permissions live in Identity; IdentityUserId has no cross-context FK.
    public sealed class ClinicalStaff
    {
        private readonly List<ClinicStaffAffiliation> _clinicAffiliations = [];

        private ClinicalStaff() { }

        public Guid Id { get; private set; }
        public Guid? IdentityUserId { get; private set; }
        public string GivenNames { get; private set; } = string.Empty;
        public string FamilyNames { get; private set; } = string.Empty;
        public ClinicalStaffType Type { get; private set; }
        public StaffStatus Status { get; private set; }
        public string? ProfessionalLicenseNumber { get; private set; }
        public string? Specialty { get; private set; }
        public string? PhoneNumber { get; private set; }

        public IReadOnlyCollection<ClinicStaffAffiliation> ClinicAffiliations => _clinicAffiliations.AsReadOnly();

        public static ClinicalStaff Register(
            string givenNames,
            string familyNames,
            ClinicalStaffType type,
            string? professionalLicenseNumber = null,
            string? specialty = null,
            string? phoneNumber = null) =>
            new()
            {
                Id = Guid.NewGuid(),
                GivenNames = Guard.Text(givenNames, FieldLengths.PersonName, nameof(givenNames)),
                FamilyNames = Guard.Text(familyNames, FieldLengths.PersonName, nameof(familyNames)),
                Type = Guard.Defined(type, nameof(type)),
                Status = StaffStatus.Active,
                ProfessionalLicenseNumber = Guard.OptionalText(
                    professionalLicenseNumber, FieldLengths.LicenseNumber, nameof(professionalLicenseNumber))?.ToUpperInvariant(),
                Specialty = Guard.OptionalText(specialty, FieldLengths.Specialty, nameof(specialty)),
                PhoneNumber = Guard.OptionalText(phoneNumber, FieldLengths.PhoneNumber, nameof(phoneNumber))
            };

        public void LinkIdentityUser(Guid identityUserId)
        {
            Guard.Id(identityUserId, nameof(identityUserId));
            if (IdentityUserId.HasValue && IdentityUserId != identityUserId)
            {
                throw new DomainException("The staff member is already linked to another account.");
            }

            IdentityUserId = identityUserId;
        }

        public void Deactivate() => Status = StaffStatus.Inactive;

        public void Activate() => Status = StaffStatus.Active;
    }
}
