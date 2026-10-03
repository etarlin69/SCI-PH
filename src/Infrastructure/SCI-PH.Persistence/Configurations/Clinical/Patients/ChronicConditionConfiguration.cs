using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Patients;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Patients
{
    internal sealed class ChronicConditionConfiguration : IEntityTypeConfiguration<ChronicCondition>
    {
        public void Configure(EntityTypeBuilder<ChronicCondition> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Name).HasMaxLength(FieldLengths.Name);
            builder.Property(x => x.CodeSystem).HasMaxLength(FieldLengths.CodeSystem);
            builder.Property(x => x.Code).HasMaxLength(FieldLengths.Code);
            builder.Property(x => x.Notes).HasMaxLength(FieldLengths.LongText);

            builder.HasOne<PatientMedicalHistory>().WithMany(x => x.ChronicConditions).HasForeignKey(x => x.MedicalHistoryId);

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_ChronicConditions_Name", NotBlank(nameof(ChronicCondition.Name)));
                table.HasCheckConstraint("CK_ChronicConditions_Code",
                    Implies(IsNotNull(nameof(ChronicCondition.Code)), IsNotNull(nameof(ChronicCondition.CodeSystem))));
            });
        }
    }
}
