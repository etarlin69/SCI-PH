using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Patients;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Patients
{
    internal sealed class PatientIdentifierConfiguration : IEntityTypeConfiguration<PatientIdentifier>
    {
        public void Configure(EntityTypeBuilder<PatientIdentifier> builder)
        {
            const string status = nameof(PatientIdentifier.Status);
            const string revokedAt = nameof(PatientIdentifier.RevokedAtUtc);
            const string createdAt = nameof(PatientIdentifier.CreatedAtUtc);
            var active = Is(status, PatientIdentifierStatus.Active);

            builder.HasKey(x => x.Id);
            // Target of IdentificationAttempt's composite key: the matched identifier must belong to the matched patient.
            builder.HasAlternateKey(x => new { x.Id, x.PatientId });
            builder.Property(x => x.Value).HasMaxLength(FieldLengths.IdentifierValue);
            builder.Property(x => x.NormalizedValue).HasMaxLength(FieldLengths.IdentifierValue);
            builder.Property(x => x.Issuer).HasMaxLength(FieldLengths.Issuer);

            builder.HasOne(x => x.Patient).WithMany(x => x.Identifiers).HasForeignKey(x => x.PatientId);

            // An active QR, national id, passport or internal code can belong to only one patient.
            builder.HasIndex(x => new { x.Type, x.Issuer, x.NormalizedValue })
                .IsUnique()
                .HasDatabaseName("UX_PatientIdentifiers_ActiveValue")
                .HasFilter(All(In(nameof(PatientIdentifier.Type), PatientIdentifier.UniqueTypes), active));
            builder.HasIndex(x => new { x.PatientId, x.Type })
                .IsUnique()
                .HasDatabaseName("UX_PatientIdentifiers_ActivePrimary")
                .HasFilter(All(IsTrue(nameof(PatientIdentifier.IsPrimary)), active));
            builder.HasIndex(x => new { x.Type, x.NormalizedValue }).HasDatabaseName("IX_PatientIdentifiers_Lookup");

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_PatientIdentifiers_Values",
                    All(NotBlank(nameof(PatientIdentifier.Value)), NotBlank(nameof(PatientIdentifier.NormalizedValue))));
                table.HasCheckConstraint("CK_PatientIdentifiers_Revocation", Any(
                    All(active, IsNull(revokedAt)),
                    All(Is(status, PatientIdentifierStatus.Revoked), IsNotNull(revokedAt), $"NOT {IsTrue(nameof(PatientIdentifier.IsPrimary))}")));
                table.HasCheckConstraint("CK_PatientIdentifiers_Verification",
                    $"{IsTrue(nameof(PatientIdentifier.IsVerified))} = ({IsNotNull(nameof(PatientIdentifier.VerifiedAtUtc))})");
                table.HasCheckConstraint("CK_PatientIdentifiers_Dates", All(
                    After(nameof(PatientIdentifier.ValidUntilUtc), createdAt),
                    NotBefore(nameof(PatientIdentifier.VerifiedAtUtc), createdAt),
                    NotBefore(revokedAt, createdAt)));
            });
        }
    }
}
