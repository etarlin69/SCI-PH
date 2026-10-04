using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Medications;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Medications
{
    internal sealed class MedicationAdministrationConfiguration : IEntityTypeConfiguration<MedicationAdministration>
    {
        private const int DosePrecision = 12;
        private const int DoseScale = 3;

        public void Configure(EntityTypeBuilder<MedicationAdministration> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Dose).HasPrecision(DosePrecision, DoseScale);
            builder.Property(x => x.DoseUnit).HasMaxLength(FieldLengths.MeasurementUnit);
            builder.Property(x => x.Notes).HasMaxLength(FieldLengths.LongText);

            builder.HasOne<ClinicalEncounter>().WithMany(x => x.MedicationAdministrations).HasForeignKey(x => x.ClinicalEncounterId);
            builder.HasOne(x => x.Medication).WithMany().HasForeignKey(x => x.MedicationId);
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.AdministeredByStaffId);
            builder.HasOne<Prescription>().WithMany()
                .HasForeignKey(x => new { x.PrescriptionId, x.ClinicalEncounterId })
                .HasPrincipalKey(x => new { x.Id, x.ClinicalEncounterId });
            builder.HasOne<PrescriptionDetail>().WithMany()
                .HasForeignKey(x => new { x.PrescriptionDetailId, x.PrescriptionId })
                .HasPrincipalKey(x => new { x.Id, x.PrescriptionId });

            builder.HasIndex(x => new { x.ClinicalEncounterId, x.AdministeredAtUtc });

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_MedicationAdministrations_Dose", All(
                    GreaterThanZero(nameof(MedicationAdministration.Dose)),
                    NotBlank(nameof(MedicationAdministration.DoseUnit))));
                table.HasCheckConstraint("CK_MedicationAdministrations_Prescription",
                    Implies(IsNotNull(nameof(MedicationAdministration.PrescriptionDetailId)),
                        IsNotNull(nameof(MedicationAdministration.PrescriptionId))));
                table.HasCheckConstraint("CK_MedicationAdministrations_EnteredInError",
                    Implies(Is(nameof(MedicationAdministration.Status), MedicationAdministrationStatus.EnteredInError),
                        IsNotNull(nameof(MedicationAdministration.Notes))));
            });
        }
    }
}
