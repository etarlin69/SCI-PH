using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Medications;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Medications
{
    internal sealed class MedicationConfiguration : IEntityTypeConfiguration<Medication>
    {
        public void Configure(EntityTypeBuilder<Medication> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Code).HasMaxLength(FieldLengths.Code);
            builder.Property(x => x.Name).HasMaxLength(FieldLengths.Name);
            builder.Property(x => x.Presentation).HasMaxLength(FieldLengths.Code);
            builder.Property(x => x.Concentration).HasMaxLength(FieldLengths.Code);

            builder.HasIndex(x => x.Code).IsUnique();
            builder.HasIndex(x => x.Name);

            builder.ToTable(table => table.HasCheckConstraint("CK_Medications_Name", NotBlank(nameof(Medication.Name))));
        }
    }
}
