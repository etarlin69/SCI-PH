using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Organizations;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Organizations
{
    internal sealed class ClinicalStaffConfiguration : IEntityTypeConfiguration<ClinicalStaff>
    {
        public void Configure(EntityTypeBuilder<ClinicalStaff> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.GivenNames).HasMaxLength(FieldLengths.PersonName);
            builder.Property(x => x.FamilyNames).HasMaxLength(FieldLengths.PersonName);
            builder.Property(x => x.ProfessionalLicenseNumber).HasMaxLength(FieldLengths.LicenseNumber);
            builder.Property(x => x.Specialty).HasMaxLength(FieldLengths.Specialty);
            builder.Property(x => x.PhoneNumber).HasMaxLength(FieldLengths.PhoneNumber);

            builder.HasIndex(x => x.IdentityUserId).IsUnique();
            builder.HasIndex(x => x.ProfessionalLicenseNumber).IsUnique();

            builder.ToTable(table => table.HasCheckConstraint("CK_ClinicalStaffMembers_Names",
                All(NotBlank(nameof(ClinicalStaff.GivenNames)), NotBlank(nameof(ClinicalStaff.FamilyNames)))));
        }
    }
}
