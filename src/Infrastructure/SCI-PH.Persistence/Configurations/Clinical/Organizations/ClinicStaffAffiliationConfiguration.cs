using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Entities.Organizations;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Organizations
{
    internal sealed class ClinicStaffAffiliationConfiguration : IEntityTypeConfiguration<ClinicStaffAffiliation>
    {
        public void Configure(EntityTypeBuilder<ClinicStaffAffiliation> builder)
        {
            builder.HasKey(x => x.Id);

            builder.HasOne<Clinic>().WithMany(x => x.StaffAffiliations).HasForeignKey(x => x.ClinicId);
            builder.HasOne<ClinicalStaff>().WithMany(x => x.ClinicAffiliations).HasForeignKey(x => x.ClinicalStaffId);

            // Only the open period is unique, so a person can leave and later return to the same clinic.
            builder.HasIndex(x => new { x.ClinicId, x.ClinicalStaffId })
                .IsUnique()
                .HasDatabaseName("UX_ClinicStaffAffiliations_Active")
                .HasFilter(IsNull(nameof(ClinicStaffAffiliation.EndedAtUtc)));

            builder.ToTable(table => table.HasCheckConstraint("CK_ClinicStaffAffiliations_Dates",
                NotBefore(nameof(ClinicStaffAffiliation.EndedAtUtc), nameof(ClinicStaffAffiliation.StartedAtUtc))));
        }
    }
}
