using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Medications;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Medications
{
    internal sealed class PrescriptionDetailConfiguration : IEntityTypeConfiguration<PrescriptionDetail>
    {
        private const int DosePrecision = 12;
        private const int DoseScale = 3;

        public void Configure(EntityTypeBuilder<PrescriptionDetail> builder)
        {
            builder.HasKey(x => x.Id);
            // Target of MedicationAdministration's composite key: the detail must belong to the referenced prescription.
            builder.HasAlternateKey(x => new { x.Id, x.PrescriptionId });
            builder.Property(x => x.Dose).HasPrecision(DosePrecision, DoseScale);
            builder.Property(x => x.DoseUnit).HasMaxLength(FieldLengths.MeasurementUnit);
            builder.Property(x => x.Frequency).HasMaxLength(FieldLengths.Frequency);
            builder.Property(x => x.Instructions).HasMaxLength(FieldLengths.LongText);

            builder.HasOne<Prescription>().WithMany(x => x.Details).HasForeignKey(x => x.PrescriptionId);
            builder.HasOne(x => x.Medication).WithMany().HasForeignKey(x => x.MedicationId);

            builder.ToTable(table => table.HasCheckConstraint("CK_PrescriptionDetails_Values", All(
                GreaterThanZero(nameof(PrescriptionDetail.Dose)),
                GreaterThanZero(nameof(PrescriptionDetail.DurationDays)),
                NotBlank(nameof(PrescriptionDetail.DoseUnit)),
                NotBlank(nameof(PrescriptionDetail.Frequency)))));
        }
    }
}
