namespace SCI_PH.Domain.Entities;

public enum ClinicalEncounterType
{
    Consultation = 0,
    Emergency = 1,
    Prehospital = 2
}

public enum ClinicalEncounterStatus
{
    Open = 0,
    Completed = 1,
    Cancelled = 2
}

public enum ClinicalNoteType
{
    Progress = 0,
    Triage = 1,
    Discharge = 2,
    Other = 3
}

public enum VitalSignType
{
    HeartRate = 0,
    RespiratoryRate = 1,
    SystolicBloodPressure = 2,
    DiastolicBloodPressure = 3,
    Temperature = 4,
    OxygenSaturation = 5,
    Weight = 6,
    Height = 7,
    BloodGlucose = 8,
    Other = 9
}

public sealed class ClinicalEncounter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    // Optional because prehospital care can start before a destination clinic is known.
    public Guid? ClinicId { get; set; }
    public Clinic? Clinic { get; set; }
    public ClinicalEncounterType Type { get; set; }
    public ClinicalEncounterStatus Status { get; set; } = ClinicalEncounterStatus.Open;
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedAtUtc { get; set; }
    public string? ChiefComplaint { get; set; }

    public ICollection<EncounterStaffAssignment> StaffAssignments { get; set; } = new List<EncounterStaffAssignment>();
    public ICollection<TriageAssessment> TriageAssessments { get; set; } = new List<TriageAssessment>();
    public ICollection<VitalSign> VitalSigns { get; set; } = new List<VitalSign>();
    public ICollection<ClinicalNote> Notes { get; set; } = new List<ClinicalNote>();
    public ICollection<EncounterDiagnosis> Diagnoses { get; set; } = new List<EncounterDiagnosis>();
    public ICollection<ClinicalTransfer> Transfers { get; set; } = new List<ClinicalTransfer>();
    public ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
    public ICollection<MedicationAdministration> MedicationAdministrations { get; set; } = new List<MedicationAdministration>();
}

public sealed class EncounterStaffAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicalEncounterId { get; set; }
    public ClinicalEncounter Encounter { get; set; } = null!;
    public Guid ClinicalStaffId { get; set; }
    public ClinicalStaff Staff { get; set; } = null!;
    public DateTimeOffset AssignedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UnassignedAtUtc { get; set; }
}

// Each assessment is separate so a triage can be reassessed during an encounter.
public sealed class TriageAssessment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicalEncounterId { get; set; }
    public ClinicalEncounter Encounter { get; set; } = null!;
    public Guid? AssessedByStaffId { get; set; }
    public ClinicalStaff? AssessedByStaff { get; set; }
    public DateTimeOffset AssessedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int? PriorityLevel { get; set; }
    public string? PriorityLabel { get; set; }
    public string? Notes { get; set; }
}

public sealed class VitalSign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicalEncounterId { get; set; }
    public ClinicalEncounter Encounter { get; set; } = null!;
    public Guid? TriageAssessmentId { get; set; }
    public TriageAssessment? TriageAssessment { get; set; }
    public Guid? RecordedByStaffId { get; set; }
    public ClinicalStaff? RecordedByStaff { get; set; }
    public VitalSignType Type { get; set; }
    public decimal Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTimeOffset MeasuredAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ClinicalNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicalEncounterId { get; set; }
    public ClinicalEncounter Encounter { get; set; } = null!;
    public Guid? AuthorStaffId { get; set; }
    public ClinicalStaff? AuthorStaff { get; set; }
    public ClinicalNoteType Type { get; set; } = ClinicalNoteType.Progress;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class EncounterDiagnosis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicalEncounterId { get; set; }
    public ClinicalEncounter Encounter { get; set; } = null!;
    public Guid? RecordedByStaffId { get; set; }
    public ClinicalStaff? RecordedByStaff { get; set; }
    public string? CodeSystem { get; set; }
    public string? Code { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ClinicalTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicalEncounterId { get; set; }
    public ClinicalEncounter Encounter { get; set; } = null!;
    public Guid? FromClinicId { get; set; }
    public Clinic? FromClinic { get; set; }
    public Guid? ToClinicId { get; set; }
    public Clinic? ToClinic { get; set; }
    public DateTimeOffset TransferredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? Reason { get; set; }
    public string? Notes { get; set; }
}
