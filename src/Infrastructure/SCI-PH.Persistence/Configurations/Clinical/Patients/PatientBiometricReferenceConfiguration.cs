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
        public void Configure(EntityTypeBuilder<PatientBiometricReference> builder)
        {
            const string status = nameof(PatientBiometricReference.Status);
            const string revokedAt = nameof(PatientBiometricReference.RevokedAtUtc);

            builder.HasKey(x => x.Id);
            // Target of IdentificationAttempt's composite key: the matched reference must belong to the matched patient.
            builder.HasAlternateKey(x => new { x.Id, x.PatientId });
            builder.Property(x => x.Provider).HasMaxLength(FieldLengths.ProviderName);
            builder.Property(x => x.SubjectReference).HasMaxLength(FieldLengths.ExternalReference);
            builder.Property(x => x.AlgorithmVersion).HasMaxLength(FieldLengths.Code);

            builder.HasOne(x => x.Patient).WithMany(x => x.BiometricReferences).HasForeignKey(x => x.PatientId);
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.RegisteredByStaffId);

            builder.HasIndex(x => new { x.Provider, x.Modality, x.SubjectReference }).IsUnique();

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
