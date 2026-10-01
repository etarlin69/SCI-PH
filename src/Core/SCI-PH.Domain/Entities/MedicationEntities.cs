namespace SCI_PH.Domain.Entities;

public enum PrescriptionStatus
{
    Draft = 0,
    Issued = 1,
    Cancelled = 2,
    Expired = 3
}

public enum HomeMedicationStatus
{
    Active = 0,
    Stopped = 1,
    Unknown = 2
}

public sealed class Medication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Code { get; set; }
    public string GenericName { get; set; } = string.Empty;
    public string? BrandName { get; set; }
    public string? Strength { get; set; }
    public string? DosageForm { get; set; }
    public string? DefaultRoute { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<PrescriptionItem> PrescriptionItems { get; set; } = new List<PrescriptionItem>();
    public ICollection<MedicationAdministration> Administrations { get; set; } = new List<MedicationAdministration>();
}

// A prescription is issued to the patient to obtain medication from a pharmacy.
// This model does not claim that a pharmacy dispensed it or that the patient took it.
public sealed class Prescription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid ClinicalEncounterId { get; set; }
    public ClinicalEncounter Encounter { get; set; } = null!;
    public Guid PrescribedByStaffId { get; set; }
    public ClinicalStaff PrescribedByStaff { get; set; } = null!;
    public PrescriptionStatus Status { get; set; } = PrescriptionStatus.Draft;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? IssuedAtUtc { get; set; }
    public DateTimeOffset? ValidUntilUtc { get; set; }
    public string? Notes { get; set; }
    public ICollection<PrescriptionItem> Items { get; set; } = new List<PrescriptionItem>();
}

public sealed class PrescriptionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PrescriptionId { get; set; }
    public Prescription Prescription { get; set; } = null!;
    public Guid MedicationId { get; set; }
    public Medication Medication { get; set; } = null!;
    public string DoseInstructions { get; set; } = string.Empty;
    public string? FrequencyInstructions { get; set; }
    public string? Route { get; set; }
    public int? DurationDays { get; set; }
    public decimal? QuantityPrescribed { get; set; }
    public string? QuantityUnit { get; set; }
    public int AuthorizedRefills { get; set; }
}

public sealed class HomeMedication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MedicalHistoryId { get; set; }
    public PatientMedicalHistory MedicalHistory { get; set; } = null!;
    public Guid? MedicationId { get; set; }
    public Medication? Medication { get; set; }
    public string MedicationName { get; set; } = string.Empty;
    public string? DoseInstructions { get; set; }
    public string? FrequencyInstructions { get; set; }
    public string? Route { get; set; }
    public HomeMedicationStatus Status { get; set; } = HomeMedicationStatus.Unknown;
    public DateOnly? StartedOn { get; set; }
    public DateOnly? StoppedOn { get; set; }
    public string? Notes { get; set; }
}
