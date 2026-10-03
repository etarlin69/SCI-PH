using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Transport;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Transport
{
    internal sealed class AmbulanceConfiguration : IEntityTypeConfiguration<Ambulance>
    {
        public void Configure(EntityTypeBuilder<Ambulance> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Code).HasMaxLength(FieldLengths.UnitCode);
            builder.Property(x => x.PlateNumber).HasMaxLength(FieldLengths.PlateNumber);
            builder.Property(x => x.CallSign).HasMaxLength(FieldLengths.UnitCode);

            builder.HasOne<Clinic>().WithMany().HasForeignKey(x => x.BaseClinicId);

            builder.HasIndex(x => x.Code).IsUnique();

            builder.ToTable(table => table.HasCheckConstraint("CK_Ambulances_Code", NotBlank(nameof(Ambulance.Code))));
        }
    }
}
