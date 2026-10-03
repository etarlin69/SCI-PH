using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Organizations;

namespace SCI_PH.Domain.Entities.Organizations
{
    public sealed class Clinic
    {
        private readonly List<ClinicStaffAffiliation> _staffAffiliations = [];
        private readonly List<ClinicalEncounter> _encounters = [];

        private Clinic() { }

        public Guid Id { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public ClinicType Type { get; private set; }
        // Facility code from the health authority, when the clinic has one.
        public string? FacilityCode { get; private set; }
        public string Province { get; private set; } = string.Empty;
        public string Municipality { get; private set; } = string.Empty;
        public string? Address { get; private set; }
        public string? PhoneNumber { get; private set; }
        public ClinicStatus Status { get; private set; }

        public IReadOnlyCollection<ClinicStaffAffiliation> StaffAffiliations => _staffAffiliations.AsReadOnly();
        public IReadOnlyCollection<ClinicalEncounter> Encounters => _encounters.AsReadOnly();

        public static Clinic Register(
            string name,
            ClinicType type,
            string province,
            string municipality,
            string? facilityCode = null,
            string? address = null,
            string? phoneNumber = null) =>
            new()
            {
                Id = Guid.NewGuid(),
                Name = Guard.Text(name, FieldLengths.Name, nameof(name)),
                Type = Guard.Defined(type, nameof(type)),
                Province = Guard.Text(province, FieldLengths.Region, nameof(province)),
                Municipality = Guard.Text(municipality, FieldLengths.Region, nameof(municipality)),
                FacilityCode = Guard.OptionalText(facilityCode, FieldLengths.Code, nameof(facilityCode))?.ToUpperInvariant(),
                Address = Guard.OptionalText(address, FieldLengths.Address, nameof(address)),
                PhoneNumber = Guard.OptionalText(phoneNumber, FieldLengths.PhoneNumber, nameof(phoneNumber)),
                Status = ClinicStatus.Active
            };

        public void Deactivate() => Status = ClinicStatus.Inactive;

        public void Activate() => Status = ClinicStatus.Active;
    }
}
