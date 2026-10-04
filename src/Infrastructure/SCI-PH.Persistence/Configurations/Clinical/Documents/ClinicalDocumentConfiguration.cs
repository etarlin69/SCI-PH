using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Documents;
using SCI_PH.Domain.Enums.Documents;
using SCI_PH.Domain.ValueObjects;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Documents
{
    internal sealed class ClinicalDocumentConfiguration : IEntityTypeConfiguration<ClinicalDocument>
    {
        public void Configure(EntityTypeBuilder<ClinicalDocument> builder)
        {
            const string status = nameof(ClinicalDocument.Status);
            const string checksum = nameof(ClinicalDocument.ChecksumSha256);
            const string uploadedAt = nameof(ClinicalDocument.UploadedAtUtc);

            builder.HasKey(x => x.Id);
            builder.Property(x => x.FileName).HasMaxLength(FieldLengths.FileName);
            builder.Property(x => x.BlobPath).HasMaxLength(FieldLengths.BlobPath);
            builder.Property(x => x.ContentType).HasMaxLength(FieldLengths.ContentType);
            builder.Property(x => x.ChecksumSha256)
                .HasConversion(value => value!.Value, value => Sha256Checksum.Parse(value))
                .HasMaxLength(Sha256Checksum.Length);

            builder.HasOne(x => x.Patient).WithMany(x => x.Documents).HasForeignKey(x => x.PatientId);
            builder.HasOne(x => x.Encounter).WithMany(x => x.Documents).HasForeignKey(x => x.ClinicalEncounterId);

            builder.HasIndex(x => x.BlobPath).IsUnique();
            builder.HasIndex(x => new { x.PatientId, x.CreatedAtUtc });
            builder.HasIndex(x => new { x.ClinicalEncounterId, x.CreatedAtUtc });

            builder.ToTable(table =>
            {
                // Exactly one owner: patient record or encounter (the patient is derived from the encounter).
                table.HasCheckConstraint("CK_ClinicalDocuments_Owner",
                    ExactlyOne(nameof(ClinicalDocument.PatientId), nameof(ClinicalDocument.ClinicalEncounterId)));
                table.HasCheckConstraint("CK_ClinicalDocuments_Size", GreaterThanZero(nameof(ClinicalDocument.SizeBytes)));
                table.HasCheckConstraint("CK_ClinicalDocuments_Checksum",
                    Any(IsNull(checksum), $"{Column(checksum)} ~ {Text($"^[0-9a-f]{{{Sha256Checksum.Length}}}$")}"));
                table.HasCheckConstraint("CK_ClinicalDocuments_StorageMetadata", All(
                    NotBlank(nameof(ClinicalDocument.FileName)),
                    NotBlank(nameof(ClinicalDocument.BlobPath)),
                    NotBlank(nameof(ClinicalDocument.ContentType))));
                table.HasCheckConstraint("CK_ClinicalDocuments_Lifecycle", All(
                    Implies(Is(status, ClinicalDocumentStatus.PendingUpload), IsNull(uploadedAt)),
                    Implies(Is(status, ClinicalDocumentStatus.Available), All(IsNotNull(uploadedAt), IsNotNull(checksum))),
                    $"({Is(status, ClinicalDocumentStatus.Deleted)}) = ({IsNotNull(nameof(ClinicalDocument.DeletedAtUtc))})"));
            });
        }
    }
}
