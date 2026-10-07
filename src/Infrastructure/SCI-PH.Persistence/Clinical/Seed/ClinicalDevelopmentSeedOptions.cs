using SCI_PH.Domain.Enums.Organizations;

namespace SCI_PH.Persistence.Clinical.Seed
{
    // Development only (the host must be in the Development environment AND Enabled must be true). Links accounts
    // that already exist (created through invitations) to a demo clinic as ClinicalStaff. It never creates accounts
    // and holds no credentials; the e-mails go in User Secrets, not in the repository.
    public sealed class ClinicalDevelopmentSeedOptions
    {
        public const string SectionName = "ClinicalDevelopmentSeed";

        public bool Enabled { get; init; }
        public string ClinicName { get; init; } = "Hospital de desarrollo SCI-PH";
        public string FacilityCode { get; init; } = "DEV-SCIPH-01";
        public List<ClinicalDevelopmentStaff> Staff { get; init; } = [];
    }

    public sealed class ClinicalDevelopmentStaff
    {
        public string Email { get; init; } = string.Empty;
        public ClinicalStaffType Type { get; init; } = ClinicalStaffType.OtherClinical;
        public string? GivenNames { get; init; }
        public string? FamilyNames { get; init; }
    }
}
