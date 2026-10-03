using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Patients;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Patients
{
    internal sealed class HomeMedicationConfiguration : IEntityTypeConfiguration<HomeMedication>
    {
        public void Configure(EntityTypeBuilder<HomeMedication> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.MedicationName).HasMaxLength(FieldLengths.Name);
            builder.Property(x => x.DoseInstructions).HasMaxLength(FieldLengths.ShortText);
            builder.Property(x => x.Frequency).HasMaxLength(FieldLengths.Frequency);
            builder.Property(x => x.Notes).HasMaxLength(FieldLengths.LongText);

            builder.HasOne<PatientMedicalHistory>().WithMany(x => x.HomeMedications).HasForeignKey(x => x.MedicalHistoryId);
            builder.HasOne<Medication>().WithMany().HasForeignKey(x => x.MedicationId);

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_HomeMedications_Name", NotBlank(nameof(HomeMedication.MedicationName)));
                table.HasCheckConstraint("CK_HomeMedications_Dates",
                    NotBefore(nameof(HomeMedication.StoppedOn), nameof(HomeMedication.StartedOn)));
            });
        }
    }
}
