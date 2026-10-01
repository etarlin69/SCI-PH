using Microsoft.EntityFrameworkCore;
using SCI_PH.Domain.Entities;

namespace SCI_PH.Persistence.Contexts;

public class ClinicalDbContext : DbContext
{
    public const string Schema = "clinical";

    public ClinicalDbContext(DbContextOptions<ClinicalDbContext> options)
        : base(options)
    {
    }

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<ClinicalStaff> ClinicalStaff => Set<ClinicalStaff>();
    public DbSet<ClinicStaffAffiliation> ClinicStaffAffiliations => Set<ClinicStaffAffiliation>();
    public DbSet<ClinicalEncounter> ClinicalEncounters => Set<ClinicalEncounter>();
    public DbSet<EncounterStaffAssignment> EncounterStaffAssignments => Set<EncounterStaffAssignment>();
    public DbSet<TriageAssessment> TriageAssessments => Set<TriageAssessment>();
    public DbSet<VitalSign> VitalSigns => Set<VitalSign>();
    public DbSet<ClinicalNote> ClinicalNotes => Set<ClinicalNote>();
    public DbSet<EncounterDiagnosis> EncounterDiagnoses => Set<EncounterDiagnosis>();
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
            entity.Property(x => x.NationalId).HasMaxLength(20);
            entity.Property(x => x.GivenNames).HasMaxLength(120).IsRequired();
            entity.Property(x => x.FamilyNames).HasMaxLength(120).IsRequired();
            entity.Property(x => x.SexAtBirth).HasMaxLength(24);
            entity.Property(x => x.PhoneNumber).HasMaxLength(32);
            entity.Property(x => x.Email).HasMaxLength(254);
            entity.Property(x => x.IdentityUserId).HasMaxLength(450);
            entity.HasIndex(x => x.NationalId).IsUnique();
            entity.HasIndex(x => x.IdentityUserId).IsUnique();
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

        builder.Entity<ClinicStaffAffiliation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ClinicId, x.ClinicalStaffId }).IsUnique();
            entity.HasOne(x => x.Clinic).WithMany(x => x.StaffAffiliations)
                .HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Staff).WithMany(x => x.ClinicAffiliations)
                .HasForeignKey(x => x.ClinicalStaffId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ClinicalEncounter>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ChiefComplaint).HasMaxLength(2000);
            entity.HasIndex(x => new { x.PatientId, x.StartedAtUtc });
            entity.HasIndex(x => new { x.ClinicId, x.StartedAtUtc });
            entity.HasOne(x => x.Patient).WithMany(x => x.Encounters)
                .HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Clinic).WithMany(x => x.Encounters)
                .HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<EncounterStaffAssignment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ClinicalEncounterId, x.ClinicalStaffId }).IsUnique();
            entity.HasOne(x => x.Encounter).WithMany(x => x.StaffAssignments)
                .HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Staff).WithMany(x => x.EncounterAssignments)
                .HasForeignKey(x => x.ClinicalStaffId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<TriageAssessment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PriorityLabel).HasMaxLength(80);
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.HasIndex(x => new { x.ClinicalEncounterId, x.AssessedAtUtc });
            entity.HasOne(x => x.Encounter).WithMany(x => x.TriageAssessments)
                .HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AssessedByStaff).WithMany()
                .HasForeignKey(x => x.AssessedByStaffId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<VitalSign>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Value).HasPrecision(12, 3);
            entity.Property(x => x.Unit).HasMaxLength(24).IsRequired();
            entity.HasIndex(x => new { x.ClinicalEncounterId, x.MeasuredAtUtc });
            entity.HasOne(x => x.Encounter).WithMany(x => x.VitalSigns)
                .HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.TriageAssessment).WithMany()
                .HasForeignKey(x => x.TriageAssessmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.RecordedByStaff).WithMany()
                .HasForeignKey(x => x.RecordedByStaffId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ClinicalNote>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Content).HasMaxLength(8000).IsRequired();
            entity.HasIndex(x => new { x.ClinicalEncounterId, x.CreatedAtUtc });
            entity.HasOne(x => x.Encounter).WithMany(x => x.Notes)
                .HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AuthorStaff).WithMany()
                .HasForeignKey(x => x.AuthorStaffId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<EncounterDiagnosis>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CodeSystem).HasMaxLength(32);
            entity.Property(x => x.Code).HasMaxLength(64);
            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.HasOne(x => x.Encounter).WithMany(x => x.Diagnoses)
                .HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.RecordedByStaff).WithMany()
                .HasForeignKey(x => x.RecordedByStaffId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ClinicalTransfer>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.Property(x => x.Notes).HasMaxLength(4000);
            entity.HasOne(x => x.Encounter).WithMany(x => x.Transfers)
                .HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.FromClinic).WithMany()
                .HasForeignKey(x => x.FromClinicId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.ToClinic).WithMany()
                .HasForeignKey(x => x.ToClinicId).OnDelete(DeleteBehavior.Restrict);
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

        builder.Entity<Prescription>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasIndex(x => new { x.PatientId, x.CreatedAtUtc });
            entity.HasOne(x => x.Patient).WithMany(x => x.Prescriptions)
                .HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Encounter).WithMany(x => x.Prescriptions)
                .HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.PrescribedByStaff).WithMany()
                .HasForeignKey(x => x.PrescribedByStaffId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PrescriptionItem>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DoseInstructions).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.FrequencyInstructions).HasMaxLength(250);
            entity.Property(x => x.Route).HasMaxLength(80);
            entity.Property(x => x.QuantityPrescribed).HasPrecision(12, 3);
            entity.Property(x => x.QuantityUnit).HasMaxLength(50);
            entity.HasOne(x => x.Prescription).WithMany(x => x.Items)
                .HasForeignKey(x => x.PrescriptionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Medication).WithMany(x => x.PrescriptionItems)
                .HasForeignKey(x => x.MedicationId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MedicationAdministration>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Dose).HasPrecision(12, 3);
            entity.Property(x => x.DoseUnit).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Route).HasMaxLength(80);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasIndex(x => new { x.ClinicalEncounterId, x.AdministeredAtUtc });
            entity.HasOne(x => x.Encounter).WithMany(x => x.MedicationAdministrations)
                .HasForeignKey(x => x.ClinicalEncounterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Medication).WithMany(x => x.Administrations)
                .HasForeignKey(x => x.MedicationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.AdministeredByStaff).WithMany()
                .HasForeignKey(x => x.AdministeredByStaffId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
