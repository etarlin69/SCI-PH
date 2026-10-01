using Microsoft.EntityFrameworkCore;
using SCI_PH.Domain.Entities;

namespace SCI_PH.Persistence.Contexts;

public class ClinicalDbContext : DbContext
{
    public const string Schema = "clinical";

    public ClinicalDbContext(DbContextOptions<ClinicalDbContext> options) : base(options) { }

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<PatientIdentifier> PatientIdentifiers => Set<PatientIdentifier>();
    public DbSet<PatientMedicalHistory> MedicalHistories => Set<PatientMedicalHistory>();
    public DbSet<PatientAllergy> Allergies => Set<PatientAllergy>();
    public DbSet<ChronicCondition> ChronicConditions => Set<ChronicCondition>();
    public DbSet<HomeMedication> HomeMedications => Set<HomeMedication>();
    public DbSet<PatientSurgicalHistory> SurgicalHistories => Set<PatientSurgicalHistory>();
    public DbSet<BiometricIdentificationAttempt> BiometricIdentificationAttempts => Set<BiometricIdentificationAttempt>();
    public DbSet<ClinicalAuditEntry> ClinicalAuditEntries => Set<ClinicalAuditEntry>();
    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<ClinicalStaff> ClinicalStaff => Set<ClinicalStaff>();
    public DbSet<ClinicStaffAffiliation> ClinicStaffAffiliations => Set<ClinicStaffAffiliation>();
    public DbSet<ClinicalEncounter> ClinicalEncounters => Set<ClinicalEncounter>();
    public DbSet<Ambulance> Ambulances => Set<Ambulance>();
    public DbSet<EncounterStaffAssignment> EncounterStaffAssignments => Set<EncounterStaffAssignment>();
    public DbSet<TriageAssessment> TriageAssessments => Set<TriageAssessment>();
    public DbSet<VitalSign> VitalSigns => Set<VitalSign>();
    public DbSet<ClinicalNote> ClinicalNotes => Set<ClinicalNote>();
    public DbSet<EncounterDiagnosis> EncounterDiagnoses => Set<EncounterDiagnosis>();
    public DbSet<ClinicalProcedure> ClinicalProcedures => Set<ClinicalProcedure>();
    public DbSet<ClinicalResult> ClinicalResults => Set<ClinicalResult>();
    public DbSet<ClinicalDocument> ClinicalDocuments => Set<ClinicalDocument>();
    public DbSet<ClinicalTransfer> ClinicalTransfers => Set<ClinicalTransfer>();
    public DbSet<Medication> Medications => Set<Medication>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();
    public DbSet<MedicationAdministration> MedicationAdministrations => Set<MedicationAdministration>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.Entity<Patient>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.GivenNames).HasMaxLength(120).IsRequired();
            entity.Property(x => x.FamilyNames).HasMaxLength(120).IsRequired();
            entity.Property(x => x.SexAtBirth).HasMaxLength(24);
            entity.Property(x => x.IdentityUserId).HasMaxLength(450);
            entity.HasIndex(x => x.IdentityUserId).IsUnique();
        });
        builder.Entity<PatientIdentifier>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Value).HasMaxLength(250).IsRequired();
            entity.Property(x => x.NormalizedValue).HasMaxLength(250).IsRequired();
            entity.Property(x => x.Issuer).HasMaxLength(120);
            entity.HasIndex(x => new { x.Type, x.NormalizedValue }).IsUnique().HasFilter(\"Type\" IN (0, 1, 2, 3));
            entity.HasIndex(x => x.PatientId);
            entity.HasOne(x => x.Patient).WithMany(x => x.Identifiers).HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PatientMedicalHistory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.HasIndex(x => new { x.PatientId, x.Version }).IsUnique();
            entity.HasOne(x => x.Patient).WithMany(x => x.MedicalHistories).HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.RecordedByStaff).WithMany().HasForeignKey(x => x.RecordedByStaffId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PatientAllergy>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Substance).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Reaction).HasMaxLength(500);
            entity.Property(x => x.Severity).HasMaxLength(40);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasOne(x => x.MedicalHistory).WithMany(x => x.Allergies).HasForeignKey(x => x.MedicalHistoryId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ChronicCondition>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CodeSystem).HasMaxLength(40);
            entity.Property(x => x.Code).HasMaxLength(80);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasOne(x => x.MedicalHistory).WithMany(x => x.ChronicConditions).HasForeignKey(x => x.MedicalHistoryId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PatientSurgicalHistory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProcedureName).HasMaxLength(250).IsRequired();
            entity.Property(x => x.Facility).HasMaxLength(180);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasOne(x => x.MedicalHistory).WithMany(x => x.SurgicalHistory).HasForeignKey(x => x.MedicalHistoryId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ClinicalStaff>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.IdentityUserId).HasMaxLength(450);
            entity.Property(x => x.GivenNames).HasMaxLength(120).IsRequired();
            entity.Property(x => x.FamilyNames).HasMaxLength(120).IsRequired();
            entity.Property(x => x.ProfessionalLicenseNumber).HasMaxLength(80);
            entity.Property(x => x.Specialty).HasMaxLength(120);
            entity.Property(x => x.PhoneNumber).HasMaxLength(32);
            entity.HasIndex(x => x.IdentityUserId).IsUnique();
        });
        builder.Entity<BiometricIdentificationAttempt>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(120).IsRequired();
            entity.Property(x => x.ProviderRequestReference).HasMaxLength(200);
            entity.HasIndex(x => new { x.ClinicalEncounterId, x.AttemptedAtUtc });
            entity.HasIndex(x => new { x.MatchedPatientId, x.AttemptedAtUtc });
            entity.HasOne(x => x.Encounter).WithMany().HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.MatchedPatient).WithMany().HasForeignKey(x => x.MatchedPatientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PerformedByStaff).WithMany().HasForeignKey(x => x.PerformedByStaffId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ClinicalAuditEntry>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ActorIdentityUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.EntityName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.EntityId).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(80).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(120);
            entity.Property(x => x.ChangesJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.EntityName, x.EntityId, x.OccurredAtUtc });
            entity.HasIndex(x => new { x.PatientId, x.OccurredAtUtc });
            entity.HasOne(x => x.ActorStaff).WithMany().HasForeignKey(x => x.ActorStaffId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Encounter).WithMany().HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Clinic>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(180).IsRequired();
            entity.Property(x => x.FacilityCode).HasMaxLength(50);
            entity.Property(x => x.Province).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Municipality).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Address).HasMaxLength(250);
            entity.Property(x => x.PhoneNumber).HasMaxLength(32);
        });
        builder.Entity<ClinicStaffAffiliation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ClinicId, x.ClinicalStaffId }).IsUnique();
            entity.HasOne(x => x.Clinic).WithMany(x => x.StaffAffiliations).HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Staff).WithMany(x => x.ClinicAffiliations).HasForeignKey(x => x.ClinicalStaffId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Ambulance>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UnitCode).HasMaxLength(60).IsRequired();
            entity.Property(x => x.PlateNumber).HasMaxLength(30);
            entity.Property(x => x.CallSign).HasMaxLength(60);
            entity.HasIndex(x => x.UnitCode).IsUnique();
            entity.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ClinicalEncounter>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TemporaryPatientCode).HasMaxLength(40);
            entity.Property(x => x.ChiefComplaint).HasMaxLength(2000);
            entity.HasIndex(x => new { x.PatientId, x.StartedAtUtc });
            entity.HasIndex(x => new { x.ClinicId, x.StartedAtUtc });
            entity.HasIndex(x => x.TemporaryPatientCode).IsUnique().HasFilter("\"TemporaryPatientCode\" IS NOT NULL");
            entity.HasOne(x => x.Patient).WithMany(x => x.Encounters).HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Clinic).WithMany(x => x.Encounters).HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Ambulance).WithMany().HasForeignKey(x => x.AmbulanceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.IdentifiedByStaff).WithMany().HasForeignKey(x => x.IdentifiedByStaffId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EncounterStaffAssignment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ClinicalEncounterId, x.ClinicalStaffId }).IsUnique();
            entity.HasOne(x => x.Encounter).WithMany(x => x.StaffAssignments).HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Staff).WithMany(x => x.EncounterAssignments).HasForeignKey(x => x.ClinicalStaffId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<TriageAssessment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PriorityLabel).HasMaxLength(80);
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.HasIndex(x => new { x.ClinicalEncounterId, x.AssessedAtUtc });
            entity.HasOne(x => x.Encounter).WithMany(x => x.TriageAssessments).HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AssessedByStaff).WithMany().HasForeignKey(x => x.AssessedByStaffId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<VitalSign>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Value).HasPrecision(12, 3);
            entity.Property(x => x.Unit).HasMaxLength(24).IsRequired();
            entity.HasIndex(x => new { x.ClinicalEncounterId, x.MeasuredAtUtc });
            entity.HasOne(x => x.Encounter).WithMany(x => x.VitalSigns).HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.TriageAssessment).WithMany().HasForeignKey(x => x.TriageAssessmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.RecordedByStaff).WithMany().HasForeignKey(x => x.RecordedByStaffId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ClinicalNote>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Content).HasMaxLength(8000).IsRequired();
            entity.HasIndex(x => new { x.ClinicalEncounterId, x.CreatedAtUtc });
            entity.HasOne(x => x.Encounter).WithMany(x => x.Notes).HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AuthorStaff).WithMany().HasForeignKey(x => x.AuthorStaffId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<EncounterDiagnosis>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CodeSystem).HasMaxLength(32);
            entity.Property(x => x.Code).HasMaxLength(64);
            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.HasOne(x => x.Encounter).WithMany(x => x.Diagnoses).HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.RecordedByStaff).WithMany().HasForeignKey(x => x.RecordedByStaffId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ClinicalProcedure>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CodeSystem).HasMaxLength(40);
            entity.Property(x => x.Code).HasMaxLength(80);
            entity.Property(x => x.Name).HasMaxLength(250).IsRequired();
            entity.Property(x => x.Outcome).HasMaxLength(2000);
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.HasIndex(x => new { x.ClinicalEncounterId, x.StartedAtUtc });
            entity.HasOne(x => x.Encounter).WithMany(x => x.Procedures).HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PerformedByStaff).WithMany().HasForeignKey(x => x.PerformedByStaffId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ClinicalResult>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CodeSystem).HasMaxLength(40);
            entity.Property(x => x.Code).HasMaxLength(80);
            entity.Property(x => x.ResultText).HasMaxLength(4000);
            entity.Property(x => x.NumericValue).HasPrecision(18, 6);
            entity.Property(x => x.Unit).HasMaxLength(40);
            entity.Property(x => x.ReferenceRange).HasMaxLength(120);
            entity.HasIndex(x => new { x.ClinicalEncounterId, x.ResultedAtUtc });
            entity.HasOne(x => x.Encounter).WithMany(x => x.Results).HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Procedure).WithMany().HasForeignKey(x => x.ClinicalProcedureId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.RecordedByStaff).WithMany().HasForeignKey(x => x.RecordedByStaffId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ClinicalDocument>(entity =>
        {
                        entity.ToTable(t => t.HasCheckConstraint("CK_ClinicalDocuments_Target", \"PatientId\" IS NOT NULL OR \"ClinicalEncounterId\" IS NOT NULL"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.BlobPath).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
            entity.Property(x => x.ChecksumSha256).HasMaxLength(64);
            entity.HasIndex(x => x.BlobPath).IsUnique();
            entity.HasIndex(x => new { x.PatientId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.ClinicalEncounterId, x.CreatedAtUtc });
            entity.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Encounter).WithMany(x => x.Documents).HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.UploadedByStaff).WithMany().HasForeignKey(x => x.UploadedByStaffId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ClinicalTransfer>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.HasOne(x => x.Encounter).WithMany(x => x.Transfers).HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.FromClinic).WithMany().HasForeignKey(x => x.FromClinicId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ToClinic).WithMany().HasForeignKey(x => x.ToClinicId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Ambulance).WithMany().HasForeignKey(x => x.AmbulanceId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Medication>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(50);
            entity.Property(x => x.GenericName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.BrandName).HasMaxLength(120);
            entity.Property(x => x.Strength).HasMaxLength(80);
            entity.Property(x => x.DosageForm).HasMaxLength(80);
            entity.Property(x => x.DefaultRoute).HasMaxLength(80);
            entity.HasIndex(x => x.Code).IsUnique();
        });
        builder.Entity<HomeMedication>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.MedicationName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.DoseInstructions).HasMaxLength(1000);
            entity.Property(x => x.FrequencyInstructions).HasMaxLength(250);
            entity.Property(x => x.Route).HasMaxLength(80);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasOne(x => x.MedicalHistory).WithMany(x => x.HomeMedications).HasForeignKey(x => x.MedicalHistoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Medication).WithMany().HasForeignKey(x => x.MedicationId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Prescription>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasIndex(x => new { x.PatientId, x.CreatedAtUtc });
            entity.HasOne(x => x.Patient).WithMany(x => x.Prescriptions).HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Encounter).WithMany(x => x.Prescriptions).HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PrescribedByStaff).WithMany().HasForeignKey(x => x.PrescribedByStaffId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PrescriptionItem>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DoseInstructions).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.FrequencyInstructions).HasMaxLength(250);
            entity.Property(x => x.Route).HasMaxLength(80);
            entity.Property(x => x.QuantityPrescribed).HasPrecision(12, 3);
            entity.Property(x => x.QuantityUnit).HasMaxLength(50);
            entity.HasOne(x => x.Prescription).WithMany(x => x.Items).HasForeignKey(x => x.PrescriptionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Medication).WithMany(x => x.PrescriptionItems).HasForeignKey(x => x.MedicationId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<MedicationAdministration>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Dose).HasPrecision(12, 3);
            entity.Property(x => x.DoseUnit).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Route).HasMaxLength(80);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasIndex(x => new { x.ClinicalEncounterId, x.AdministeredAtUtc });
            entity.HasOne(x => x.Encounter).WithMany(x => x.MedicationAdministrations).HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Medication).WithMany(x => x.Administrations).HasForeignKey(x => x.MedicationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AdministeredByStaff).WithMany().HasForeignKey(x => x.AdministeredByStaffId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
