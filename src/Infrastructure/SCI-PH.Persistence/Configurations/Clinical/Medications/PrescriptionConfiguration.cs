using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Medications;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Medications
{
    internal sealed class PrescriptionConfiguration : IEntityTypeConfiguration<Prescription>
    {
        public void Configure(EntityTypeBuilder<Prescription> builder)
        {
            const string status = nameof(Prescription.Status);
            const string issuedAt = nameof(Prescription.IssuedAtUtc);
            const string cancelledAt = nameof(Prescription.CancelledAtUtc);

            builder.HasKey(x => x.Id);
            // Target of MedicationAdministration's composite key: an administration can only fulfil
            // a prescription of its own encounter.
            builder.HasAlternateKey(x => new { x.Id, x.ClinicalEncounterId });
            builder.Property(x => x.Notes).HasMaxLength(FieldLengths.LongText);

            builder.HasOne(x => x.Encounter).WithMany(x => x.Prescriptions).HasForeignKey(x => x.ClinicalEncounterId);
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.PrescribedByStaffId);

            builder.HasIndex(x => new { x.ClinicalEncounterId, x.CreatedAtUtc });

            builder.ToTable(table => table.HasCheckConstraint("CK_Prescriptions_Lifecycle", All(
                Implies(Is(status, PrescriptionStatus.Draft), All(IsNull(issuedAt), IsNull(cancelledAt))),
                Implies(Is(status, PrescriptionStatus.Issued), All(IsNotNull(issuedAt), IsNull(cancelledAt))),
                Implies(Is(status, PrescriptionStatus.Cancelled), IsNotNull(cancelledAt)),
                After(nameof(Prescription.ValidUntilUtc), issuedAt))));
        }
    }
}
