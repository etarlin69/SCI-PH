using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Organizations;

namespace SCI_PH.Persistence.Configurations.Clinical.Encounters
{
    internal sealed class TriageConfiguration : IEntityTypeConfiguration<Triage>
    {
        public void Configure(EntityTypeBuilder<Triage> builder)
        {
            builder.HasKey(x => x.Id);
            // Target of VitalSign's composite key: a reading can only point to a triage of its own encounter.
            builder.HasAlternateKey(x => new { x.Id, x.ClinicalEncounterId });
            builder.Property(x => x.Reason).HasMaxLength(FieldLengths.ShortText);
            builder.Property(x => x.Observations).HasMaxLength(FieldLengths.LongText);

            builder.HasOne(x => x.Encounter).WithMany(x => x.Triages).HasForeignKey(x => x.ClinicalEncounterId);
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.PerformedByStaffId);

            builder.HasIndex(x => new { x.ClinicalEncounterId, x.PerformedAtUtc });
        }
    }
}
