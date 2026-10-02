namespace SCI_PH.Domain.Entities
{

    public enum ClinicalEncounterType { Consultation = 0, Emergency = 1, Prehospital = 2 }
    public enum ClinicalEncounterStatus { Open = 0, Completed = 1, Cancelled = 2 }
    public enum ClinicalNoteType { Progress = 0, Triage = 1, Discharge = 2, Other = 3 }
    public enum VitalSignType { HeartRate = 0, RespiratoryRate = 1, SystolicBloodPressure = 2, DiastolicBloodPressure = 3, Temperature = 4, OxygenSaturation = 5, Weight = 6, Height = 7, BloodGlucose = 8, Other = 9 }
    public enum ClinicalProcedureStatus { Planned = 0, InProgress = 1, Completed = 2, Cancelled = 3 }
    public enum ClinicalResultStatus { Pending = 0, Final = 1, Corrected = 2, Cancelled = 3 }
    public enum ClinicalDocumentType { ClinicalNote = 0, LabResult = 1, Imaging = 2, Referral = 3, Consent = 4, Other = 5 }
    public enum ClinicalDocumentStatus { PendingUpload = 0, Available = 1, Rejected = 2, Deleted = 3 }

    public sealed class ClinicalEncounter
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        // Nullable so emergency/prehospital care can begin for an unidentified patient (NN).
        public Guid? PatientId { get; set; }
        public Patient? Patient { get; set; }
        public PatientIdentificationStatus IdentificationStatus { get; set; } = PatientIdentificationStatus.Unidentified;
        public string? TemporaryPatientCode { get; set; } = $"NN-{Guid.NewGuid():N}";
        public DateTimeOffset? IdentifiedAtUtc { get; set; }
        public Guid? IdentifiedByStaffId { get; set; }
        public ClinicalStaff? IdentifiedByStaff { get; set; }
        // Optional because prehospital care can start before a destination clinic is known.
        public Guid? ClinicId { get; set; }
        public Clinic? Clinic { get; set; }
        public Guid? AmbulanceId { get; set; }
        public Ambulance? Ambulance { get; set; }
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
        public ICollection<ClinicalProcedure> Procedures { get; set; } = new List<ClinicalProcedure>();
        public ICollection<ClinicalResult> Results { get; set; } = new List<ClinicalResult>();
        public ICollection<ClinicalDocument> Documents { get; set; } = new List<ClinicalDocument>();
        public ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
        public ICollection<MedicationAdministration> MedicationAdministrations { get; set; } = new List<MedicationAdministration>();

        // The encounter Id and all its clinical records remain unchanged when an NN is identified.
        // Save this change and its ClinicalAuditEntry in the same application transaction.
        public void IdentifyPatient(Guid patientId, Guid staffId, DateTimeOffset identifiedAtUtc)
        {
            if (patientId == Guid.Empty || staffId == Guid.Empty)
            {
                throw new ArgumentException("Patient and staff identifiers are required.");
            }

            if (PatientId.HasValue && PatientId != patientId)
            {
                throw new InvalidOperationException("Reassigning an encounter requires a separate reviewed correction workflow.");
            }

            if (IdentificationStatus == PatientIdentificationStatus.Identified)
            {
                throw new InvalidOperationException("This encounter has already been identified.");
            }

            if (identifiedAtUtc < StartedAtUtc)
            {
                throw new ArgumentOutOfRangeException(nameof(identifiedAtUtc), "Identification cannot precede the encounter.");
            }

            PatientId = patientId;
            IdentifiedByStaffId = staffId;
            IdentifiedAtUtc = identifiedAtUtc.ToUniversalTime();
            IdentificationStatus = PatientIdentificationStatus.Identified;
        }
    }

    public sealed class Ambulance
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? ClinicId { get; set; }
        public Clinic? Clinic { get; set; }
        public string UnitCode { get; set; } = string.Empty;
        public string? PlateNumber { get; set; }
        public string? CallSign { get; set; }
        public bool IsActive { get; set; } = true;
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

    public sealed class ClinicalProcedure
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ClinicalEncounterId { get; set; }
        public ClinicalEncounter Encounter { get; set; } = null!;
        public Guid? PerformedByStaffId { get; set; }
        public ClinicalStaff? PerformedByStaff { get; set; }
        public string? CodeSystem { get; set; }
        public string? Code { get; set; }
        public string Name { get; set; } = string.Empty;
        public ClinicalProcedureStatus Status { get; set; } = ClinicalProcedureStatus.Planned;
        public DateTimeOffset? StartedAtUtc { get; set; }
        public DateTimeOffset? CompletedAtUtc { get; set; }
        public string? Outcome { get; set; }
        public string? Notes { get; set; }
    }

    public sealed class ClinicalResult
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ClinicalEncounterId { get; set; }
        public ClinicalEncounter Encounter { get; set; } = null!;
        public Guid? ClinicalProcedureId { get; set; }
        public ClinicalProcedure? Procedure { get; set; }
        public Guid? RecordedByStaffId { get; set; }
        public ClinicalStaff? RecordedByStaff { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? CodeSystem { get; set; }
        public string? Code { get; set; }
        public string? ResultText { get; set; }
        public decimal? NumericValue { get; set; }
        public string? Unit { get; set; }
        public string? ReferenceRange { get; set; }
        public ClinicalResultStatus Status { get; set; } = ClinicalResultStatus.Pending;
        public DateTimeOffset? ResultedAtUtc { get; set; }
    }

    // Files live in external object storage; PostgreSQL stores metadata and the object path only.
    public sealed class ClinicalDocument
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? PatientId { get; set; }
        public Patient? Patient { get; set; }
        public Guid? ClinicalEncounterId { get; set; }
        public ClinicalEncounter? Encounter { get; set; }
        public Guid? UploadedByStaffId { get; set; }
        public ClinicalStaff? UploadedByStaff { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string BlobPath { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public ClinicalDocumentType Type { get; set; }
        public ClinicalDocumentStatus Status { get; set; } = ClinicalDocumentStatus.PendingUpload;
        public string? ChecksumSha256 { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? UploadedAtUtc { get; set; }
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
        public Guid? AmbulanceId { get; set; }
        public Ambulance? Ambulance { get; set; }
        public DateTimeOffset TransferredAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public string? Reason { get; set; }
        public string? Notes { get; set; }
    }
}
