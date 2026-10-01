namespace SCI_PH.Domain.Entities;

public enum PatientIdentityStatus
{
    Unverified = 0,
    Verified = 1,
    Temporary = 2
}

public enum ClinicType
{
    Hospital = 0,
    Clinic = 1,
    PrimaryCareCenter = 2,
    EmergencyCenter = 3,
    Other = 4
}

// Clinical roles only. Non-clinical administrative staff are not represented here.
public enum ClinicalStaffRole
{
    Physician = 0,
    MedicalAssistant = 1,
    Nurse = 2,
    Paramedic = 3,
    OtherClinical = 4
}

public sealed class Patient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? NationalId { get; set; }
    public PatientIdentityStatus IdentityStatus { get; set; } = PatientIdentityStatus.Unverified;
    public string? IdentityUserId { get; set; }
    public string GivenNames { get; set; } = string.Empty;
    public string FamilyNames { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string? SexAtBirth { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ClinicalEncounter> Encounters { get; set; } = new List<ClinicalEncounter>();
    public ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
}

public sealed class Clinic
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? FacilityCode { get; set; }
    public ClinicType Type { get; set; } = ClinicType.Clinic;
    public string Province { get; set; } = string.Empty;
    public string Municipality { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ClinicStaffAffiliation> StaffAffiliations { get; set; } = new List<ClinicStaffAffiliation>();
    public ICollection<ClinicalEncounter> Encounters { get; set; } = new List<ClinicalEncounter>();
}

public sealed class ClinicalStaff
{
    public Guid Id { get; set; } = Guid.NewGuid();
    // Identity is managed by IdentityDbContext; this value intentionally has no cross-context FK.
    public string? IdentityUserId { get; set; }
    public string GivenNames { get; set; } = string.Empty;
    public string FamilyNames { get; set; } = string.Empty;
    public ClinicalStaffRole Role { get; set; }
    public string? ProfessionalLicenseNumber { get; set; }
    public string? Specialty { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ClinicStaffAffiliation> ClinicAffiliations { get; set; } = new List<ClinicStaffAffiliation>();
    public ICollection<EncounterStaffAssignment> EncounterAssignments { get; set; } = new List<EncounterStaffAssignment>();
}

public sealed class ClinicStaffAffiliation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public Clinic Clinic { get; set; } = null!;
    public Guid ClinicalStaffId { get; set; }
    public ClinicalStaff Staff { get; set; } = null!;
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
}
