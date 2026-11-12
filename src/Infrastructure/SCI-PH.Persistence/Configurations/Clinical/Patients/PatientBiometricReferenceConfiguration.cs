using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Patients;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Patients
{
    internal sealed class PatientBiometricReferenceConfiguration : IEntityTypeConfiguration<PatientBiometricReference>
    {
        public const string ActiveSubjectIndexName = "UX_PatientBiometricReferences_ActiveSubject";
        public const string ActiveFacePerPatientIndexName = "UX_PatientBiometricReferences_ActiveFacePerPatient";

        public void Configure(EntityTypeBuilder<PatientBiometricReference> builder)
        {
            const string status = nameof(PatientBiometricReference.Status);
            const string revokedAt = nameof(PatientBiometricReference.RevokedAtUtc);
            const string modality = nameof(PatientBiometricReference.Modality);

            builder.HasKey(x => x.Id);
            // Concurrency token: a use of the reference (a fingerprint confirmation) loses against its revocation.
            builder.Property(x => x.Status).IsConcurrencyToken();
            // Target of IdentificationAttempt's composite key: the matched reference must belong to the matched patient.
            builder.HasAlternateKey(x => new { x.Id, x.PatientId });
            builder.Property(x => x.Provider).HasMaxLength(FieldLengths.ProviderName);
            builder.Property(x => x.SubjectReference).HasMaxLength(FieldLengths.ExternalReference);
            builder.Property(x => x.AlgorithmVersion).HasMaxLength(FieldLengths.Code);

            builder.HasOne(x => x.Patient).WithMany(x => x.BiometricReferences).HasForeignKey(x => x.PatientId);
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.RegisteredByStaffId);

            // Unique among ACTIVE references only: a revoked reference stays as history and its sensor slot can be used again.
            builder.HasIndex(x => new { x.Provider, x.Modality, x.SubjectReference })
                .IsUnique()
                .HasDatabaseName(ActiveSubjectIndexName)
                .HasFilter(Is(status, BiometricReferenceStatus.Active));
            // The foreign key's own (unfiltered) index, declared so the partial index below never replaces it.
            builder.HasIndex([nameof(PatientBiometricReference.PatientId)], "IX_PatientBiometricReferences_PatientId");
            // At most one ACTIVE face reference per patient (ADR-012), also under concurrent enrollments; revoked ones stay as
            // history.
            builder.HasIndex([nameof(PatientBiometricReference.PatientId)], ActiveFacePerPatientIndexName)
                .IsUnique()
                .HasFilter(All(Is(modality, BiometricModality.Face), Is(status, BiometricReferenceStatus.Active)));

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_PatientBiometricReferences_References", All(
                    NotBlank(nameof(PatientBiometricReference.Provider)),
                    NotBlank(nameof(PatientBiometricReference.SubjectReference))));
                table.HasCheckConstraint("CK_PatientBiometricReferences_Revocation", Any(
                    All(Is(status, BiometricReferenceStatus.Active), IsNull(revokedAt)),
                    All(Is(status, BiometricReferenceStatus.Revoked), IsNotNull(revokedAt),
                        NotBefore(revokedAt, nameof(PatientBiometricReference.CreatedAtUtc)))));
            });
        }
    }
}
