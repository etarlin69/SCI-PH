using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Organizations;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Encounters
{
    internal sealed class EncounterStaffAssignmentConfiguration : IEntityTypeConfiguration<EncounterStaffAssignment>
    {
        public void Configure(EntityTypeBuilder<EncounterStaffAssignment> builder)
        {
            builder.HasKey(x => x.Id);

            builder.HasOne<ClinicalEncounter>().WithMany(x => x.StaffAssignments).HasForeignKey(x => x.ClinicalEncounterId);
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.ClinicalStaffId);

            // Only the open assignment is unique, so a person can be assigned again after leaving.
            builder.HasIndex(x => new { x.ClinicalEncounterId, x.ClinicalStaffId })
                .IsUnique()
                .HasDatabaseName("UX_EncounterStaffAssignments_Active")
                .HasFilter(IsNull(nameof(EncounterStaffAssignment.UnassignedAtUtc)));

            builder.ToTable(table => table.HasCheckConstraint("CK_EncounterStaffAssignments_Dates",
                NotBefore(nameof(EncounterStaffAssignment.UnassignedAtUtc), nameof(EncounterStaffAssignment.AssignedAtUtc))));
        }
    }
}
