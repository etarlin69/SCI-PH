using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Organizations;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Encounters
{
    internal sealed class VitalSignConfiguration : IEntityTypeConfiguration<VitalSign>
    {
        private const int ValuePrecision = 8;
        private const int ValueScale = 2;

        public void Configure(EntityTypeBuilder<VitalSign> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Value).HasPrecision(ValuePrecision, ValueScale);
            builder.Property(x => x.Unit).HasMaxLength(FieldLengths.MeasurementUnit);

            builder.HasOne(x => x.Encounter).WithMany(x => x.VitalSigns).HasForeignKey(x => x.ClinicalEncounterId);
            builder.HasOne<Triage>().WithMany()
                .HasForeignKey(x => new { x.TriageId, x.ClinicalEncounterId })
                .HasPrincipalKey(x => new { x.Id, x.ClinicalEncounterId });
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.MeasuredByStaffId);

            builder.HasIndex(x => new { x.ClinicalEncounterId, x.Type, x.MeasuredAtUtc });

            builder.ToTable(table => table.HasCheckConstraint("CK_VitalSigns_Range", PlausibleValue()));
        }

        // Built from the same limits the domain validates, so both layers accept exactly the same readings.
        private static string PlausibleValue()
        {
            const string value = nameof(VitalSign.Value);
            return Any(VitalSignLimits.ByType
                .Select(limit => All(
                    Is(nameof(VitalSign.Type), limit.Key),
                    $"{Column(value)} BETWEEN {Number(limit.Value.Minimum)} AND {Number(limit.Value.Maximum)}",
                    $"{Column(nameof(VitalSign.Unit))} = {Text(limit.Value.Unit)}"))
                .ToArray());
        }
    }
}
