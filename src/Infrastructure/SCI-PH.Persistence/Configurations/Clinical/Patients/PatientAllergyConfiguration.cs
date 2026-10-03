using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Patients;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Patients
{
    internal sealed class PatientAllergyConfiguration : IEntityTypeConfiguration<PatientAllergy>
    {
        public void Configure(EntityTypeBuilder<PatientAllergy> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Substance).HasMaxLength(FieldLengths.Name);
            builder.Property(x => x.Reaction).HasMaxLength(FieldLengths.ShortText);
            builder.Property(x => x.Notes).HasMaxLength(FieldLengths.LongText);

            builder.HasOne<PatientMedicalHistory>().WithMany(x => x.Allergies).HasForeignKey(x => x.MedicalHistoryId);

            builder.ToTable(table => table.HasCheckConstraint(
                "CK_PatientAllergies_Substance", NotBlank(nameof(PatientAllergy.Substance))));
        }
    }
}
