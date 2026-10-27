using SCI_PH.Domain.Enums.Organizations;

namespace SCI_PH.Persistence.Clinical.Seed
{
    // Development only (the host must be in the Development environment AND Enabled must be true). Creates a demo
    // clinic and ClinicalStaff records affiliated with it, so an administrator can invite them
    // (POST /api/web/admin/invitations/staff links the account on activation). It never creates accounts and holds no
    // credentials. Optional e-mails (User Secrets, not the repository) link accounts that already exist. It also adds a
    // small demo medication catalog ("DEV-" codes) so prescriptions and the Kardex can be exercised. Until the fingerprint
    // enrollment exists, FingerprintReferences links existing patients to a slot of an existing reader (no template and no
    // key: only the reader's id and the slot), so the emergency identification can be tried with the device simulator.
    public sealed class ClinicalDevelopmentSeedOptions
    {
        public const string SectionName = "ClinicalDevelopmentSeed";

        public bool Enabled { get; init; }
        public string ClinicName { get; init; } = "Hospital de desarrollo SCI-PH";
        public string FacilityCode { get; init; } = "DEV-SCIPH-01";
        public List<ClinicalDevelopmentStaff> Staff { get; init; } = [];
        public List<ClinicalDevelopmentFingerprintReference> FingerprintReferences { get; init; } = [];
    }

    // Development only: the patient (already registered) whose fingerprint is in that slot of that reader
    // (DeviceCredential id, as listed by the administration). Idempotent by the resulting reference.
    public sealed class ClinicalDevelopmentFingerprintReference
    {
        public Guid PatientId { get; init; }
        public Guid DeviceCredentialId { get; init; }
        public int Slot { get; init; }
    }

    public sealed class ClinicalDevelopmentStaff
    {
        // Idempotency key (ClinicalStaff.ProfessionalLicenseNumber is unique), e.g. "DEV-NUR-01".
        public string LicenseNumber { get; init; } = string.Empty;
        public string GivenNames { get; init; } = "Personal";
        public string FamilyNames { get; init; } = "Desarrollo";
        public ClinicalStaffType Type { get; init; } = ClinicalStaffType.OtherClinical;
        // Optional: link an account that already exists (never an administrator).
        public string? Email { get; init; }
    }
}
