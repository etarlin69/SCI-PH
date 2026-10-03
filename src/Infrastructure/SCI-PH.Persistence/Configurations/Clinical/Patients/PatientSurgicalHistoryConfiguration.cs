using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Patients;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Patients
{
    internal sealed class PatientSurgicalHistoryConfiguration : IEntityTypeConfiguration<PatientSurgicalHistory>
    {
        public void Configure(EntityTypeBuilder<PatientSurgicalHistory> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.ProcedureName).HasMaxLength(FieldLengths.Name);
            builder.Property(x => x.Facility).HasMaxLength(FieldLengths.Name);
            builder.Property(x => x.Notes).HasMaxLength(FieldLengths.LongText);

            builder.HasOne<PatientMedicalHistory>().WithMany(x => x.SurgicalHistories).HasForeignKey(x => x.MedicalHistoryId);

            builder.ToTable(table => table.HasCheckConstraint(
                "CK_PatientSurgicalHistories_Procedure", NotBlank(nameof(PatientSurgicalHistory.ProcedureName))));
        }
    }
}
