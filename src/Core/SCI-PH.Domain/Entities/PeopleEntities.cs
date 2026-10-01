namespace SCI_PH.Domain.Entities;

public enum PatientIdentityStatus
{
    Unverified = 0,
    Verified = 1,
    Temporary = 2
}

public enum PatientIdentifierType
{
    NationalId = 0,
    Passport = 1,
    QrCode = 2,
    InternalCode = 3,
    Phone = 4,
    Email = 5,
    Other = 6
}

public enum PatientIdentificationStatus
{
    Unidentified = 0,
    Provisional = 1,
    Identified = 2
}

public enum BiometricModality
{
    Fingerprint = 0,
    Face = 1,
    Iris = 2,
    Voice = 3,
    Other = 4
}

public enum IdentificationAttemptOutcome
{
    Match = 0,
    NoMatch = 1,
    Inconclusive = 2,
    ProviderUnavailable = 3,
    Error = 4
}

public enum ClinicalRecordStatus
{
    Active = 0,
    Resolved = 1,
    Inactive = 2,
    Unknown = 3
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
    public PatientIdentityStatus IdentityStatus { get; set; } = PatientIdentityStatus.Unverified;
    // Identity users are managed in the separate IdentityDbContext; this is not a cross-context FK.
    public string? IdentityUserId { get; set; }
    public string GivenNames { get; set; } = string.Empty;
    public string FamilyNames { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string? SexAtBirth { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<PatientIdentifier> Identifiers { get; set; } = new List<PatientIdentifier>();
    public ICollection<PatientMedicalHistory> MedicalHistories { get; set; } = new List<PatientMedicalHistory>();
    public ICollection<ClinicalEncounter> Encounters { get; set; } = new List<ClinicalEncounter>();
    public ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
}

public sealed class PatientIdentifier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public PatientIdentifierType Type { get; set; }
    public string Value { get; set; } = string.Empty;
    public string NormalizedValue { get; set; } = string.Empty;
    public string? Issuer { get; set; }
    public bool IsVerified { get; set; }
    public bool IsPrimary { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ValidUntilUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

public sealed class PatientMedicalHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public int Version { get; set; } = 1;
    public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? RecordedByStaffId { get; set; }
    public ClinicalStaff? RecordedByStaff { get; set; }
    public string? Notes { get; set; }
    public ICollection<PatientAllergy> Allergies { get; set; } = new List<PatientAllergy>();
    public ICollection<ChronicCondition> ChronicConditions { get; set; } = new List<ChronicCondition>();
    public ICollection<HomeMedication> HomeMedications { get; set; } = new List<HomeMedication>();
    public ICollection<PatientSurgicalHistory> SurgicalHistory { get; set; } = new List<PatientSurgicalHistory>();
}

public sealed class PatientAllergy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MedicalHistoryId { get; set; }
    public PatientMedicalHistory MedicalHistory { get; set; } = null!;
    public string Substance { get; set; } = string.Empty;
    public string? Reaction { get; set; }
    public string? Severity { get; set; }
    public ClinicalRecordStatus Status { get; set; } = ClinicalRecordStatus.Active;
    public DateOnly? OnsetDate { get; set; }
    public string? Notes { get; set; }
}

public sealed class ChronicCondition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MedicalHistoryId { get; set; }
    public PatientMedicalHistory MedicalHistory { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? CodeSystem { get; set; }
    public string? Code { get; set; }
    public ClinicalRecordStatus Status { get; set; } = ClinicalRecordStatus.Active;
    public DateOnly? DiagnosedOn { get; set; }
    public string? Notes { get; set; }
}

public sealed class PatientSurgicalHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MedicalHistoryId { get; set; }
    public PatientMedicalHistory MedicalHistory { get; set; } = null!;
    public string ProcedureName { get; set; } = string.Empty;
    public DateOnly? PerformedOn { get; set; }
    public string? Facility { get; set; }
    public string? Notes { get; set; }
}

// Store the identification event and provider reference only; never persist raw biometric samples/templates here.
public sealed class BiometricIdentificationAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ClinicalEncounterId { get; set; }
    public ClinicalEncounter? Encounter { get; set; }
    public Guid? MatchedPatientId { get; set; }
    public Patient? MatchedPatient { get; set; }
    public Guid? PerformedByStaffId { get; set; }
    public ClinicalStaff? PerformedByStaff { get; set; }
    public BiometricModality Modality { get; set; }
    public IdentificationAttemptOutcome Outcome { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? ProviderRequestReference { get; set; }
    public DateTimeOffset AttemptedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

// Keep audit data append-only and redact sensitive values before writing ChangesJson.
public sealed class ClinicalAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ActorIdentityUserId { get; set; } = string.Empty;
    public Guid? ActorStaffId { get; set; }
    public ClinicalStaff? ActorStaff { get; set; }
    public Guid? PatientId { get; set; }
    public Patient? Patient { get; set; }
    public Guid? ClinicalEncounterId { get; set; }
    public ClinicalEncounter? Encounter { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; set; }
    public string? ChangesJson { get; set; }
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

// Identity is managed by IdentityDbContext; IdentityUserId intentionally has no cross-context FK.
public sealed class ClinicalStaff
{
    public Guid Id { get; set; } = Guid.NewGuid();
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
