using Microsoft.EntityFrameworkCore;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Documents;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Entities.Transport;
using SCI_PH.Persistence.Clinical.Biometrics;
using SCI_PH.Persistence.Clinical.Idempotency;
using SCI_PH.Persistence.Configurations.Clinical;
using SCI_PH.Persistence.Configurations.Clinical.Biometrics;
using SCI_PH.Persistence.Configurations.Clinical.Idempotency;

namespace SCI_PH.Persistence.Contexts
{
    public class ClinicalDbContext : DbContext
    {
        public const string Schema = "clinical";

        public ClinicalDbContext(DbContextOptions<ClinicalDbContext> options)
            : base(options)
        {
        }

        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<PatientIdentifier> PatientIdentifiers => Set<PatientIdentifier>();
        public DbSet<PatientMedicalHistory> PatientMedicalHistories => Set<PatientMedicalHistory>();
        public DbSet<PatientAllergy> PatientAllergies => Set<PatientAllergy>();
        public DbSet<ChronicCondition> ChronicConditions => Set<ChronicCondition>();
        public DbSet<HomeMedication> HomeMedications => Set<HomeMedication>();
        public DbSet<PatientSurgicalHistory> PatientSurgicalHistories => Set<PatientSurgicalHistory>();
        public DbSet<PatientBiometricReference> PatientBiometricReferences => Set<PatientBiometricReference>();
        public DbSet<IdentificationAttempt> IdentificationAttempts => Set<IdentificationAttempt>();

        public DbSet<Clinic> Clinics => Set<Clinic>();
        public DbSet<ClinicalStaff> ClinicalStaffMembers => Set<ClinicalStaff>();
        public DbSet<ClinicStaffAffiliation> ClinicStaffAffiliations => Set<ClinicStaffAffiliation>();

        public DbSet<ClinicalEncounter> ClinicalEncounters => Set<ClinicalEncounter>();
        public DbSet<EncounterStaffAssignment> EncounterStaffAssignments => Set<EncounterStaffAssignment>();
        public DbSet<Triage> Triages => Set<Triage>();
        public DbSet<VitalSign> VitalSigns => Set<VitalSign>();
        public DbSet<ClinicalNote> ClinicalNotes => Set<ClinicalNote>();
        public DbSet<Diagnosis> Diagnoses => Set<Diagnosis>();
        public DbSet<ClinicalProcedure> ClinicalProcedures => Set<ClinicalProcedure>();
        public DbSet<ClinicalResult> ClinicalResults => Set<ClinicalResult>();

        public DbSet<Medication> Medications => Set<Medication>();
        public DbSet<Prescription> Prescriptions => Set<Prescription>();
        public DbSet<PrescriptionDetail> PrescriptionDetails => Set<PrescriptionDetail>();
        public DbSet<MedicationAdministration> MedicationAdministrations => Set<MedicationAdministration>();

        public DbSet<Ambulance> Ambulances => Set<Ambulance>();
        public DbSet<ClinicalTransfer> ClinicalTransfers => Set<ClinicalTransfer>();

        public DbSet<ClinicalDocument> ClinicalDocuments => Set<ClinicalDocument>();
        public DbSet<ClinicalAuditEntry> ClinicalAuditEntries => Set<ClinicalAuditEntry>();

        // Technical state (not a Domain entity), committed with the clinical rows it protects.
        public DbSet<IdempotencyReceiptEntry> IdempotencyReceipts => Set<IdempotencyReceiptEntry>();
        public DbSet<FingerprintIdentificationSessionEntry> FingerprintIdentificationSessions => Set<FingerprintIdentificationSessionEntry>();
        public DbSet<FingerprintEnrollmentSessionEntry> FingerprintEnrollmentSessions => Set<FingerprintEnrollmentSessionEntry>();

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            EnsureAuditEntriesAreAppendOnly();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            EnsureAuditEntriesAreAppendOnly();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.HasDefaultSchema(Schema);
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ClinicalDbContext).Assembly, ConfiguresDomainEntity);
            modelBuilder.ApplyConfiguration(new IdempotencyReceiptConfiguration());
            modelBuilder.ApplyConfiguration(new FingerprintIdentificationSessionConfiguration());
            modelBuilder.ApplyConfiguration(new FingerprintEnrollmentSessionConfiguration());
            modelBuilder.RestrictAllDeletes().AddEnumCheckConstraints();
        }

        // ExecuteUpdate, ExecuteDelete and raw SQL bypass this check; do not use them on audit entries.
        private void EnsureAuditEntriesAreAppendOnly()
        {
            if (ChangeTracker.Entries<ClinicalAuditEntry>().Any(entry =>
                entry.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new InvalidOperationException("Clinical audit entries cannot be modified or deleted.");
            }
        }

        // Applies only configurations of Domain entities, never Identity ones from this assembly.
        private static bool ConfiguresDomainEntity(Type configurationType) =>
            configurationType.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)
                && contract.GenericTypeArguments[0].Assembly == typeof(Patient).Assembly);
    }
}
