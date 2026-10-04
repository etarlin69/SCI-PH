using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Organizations;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Organizations
{
    internal sealed class ClinicConfiguration : IEntityTypeConfiguration<Clinic>
    {
        public void Configure(EntityTypeBuilder<Clinic> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Name).HasMaxLength(FieldLengths.Name);
            builder.Property(x => x.FacilityCode).HasMaxLength(FieldLengths.Code);
            builder.Property(x => x.Province).HasMaxLength(FieldLengths.Region);
            builder.Property(x => x.Municipality).HasMaxLength(FieldLengths.Region);
            builder.Property(x => x.Address).HasMaxLength(FieldLengths.Address);
            builder.Property(x => x.PhoneNumber).HasMaxLength(FieldLengths.PhoneNumber);

            builder.HasIndex(x => x.FacilityCode).IsUnique();

            builder.ToTable(table => table.HasCheckConstraint("CK_Clinics_Names", All(
                NotBlank(nameof(Clinic.Name)), NotBlank(nameof(Clinic.Province)), NotBlank(nameof(Clinic.Municipality)))));
        }
    }
}
