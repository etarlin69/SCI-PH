using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Transport;
using SCI_PH.Domain.Enums.Transport;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Transport
{
    internal sealed class ClinicalTransferConfiguration : IEntityTypeConfiguration<ClinicalTransfer>
    {
        public void Configure(EntityTypeBuilder<ClinicalTransfer> builder)
        {
            const string status = nameof(ClinicalTransfer.Status);
            const string origin = nameof(ClinicalTransfer.OriginClinicId);
            const string requestedAt = nameof(ClinicalTransfer.RequestedAtUtc);
            const string sentAt = nameof(ClinicalTransfer.SentAtUtc);
            const string receivedAt = nameof(ClinicalTransfer.ReceivedAtUtc);
            const string completedAt = nameof(ClinicalTransfer.CompletedAtUtc);
            const string cancelledAt = nameof(ClinicalTransfer.CancelledAtUtc);

            builder.HasKey(x => x.Id);
            builder.Property(x => x.Reason).HasMaxLength(FieldLengths.ShortText);
            builder.Property(x => x.Notes).HasMaxLength(FieldLengths.LongText);

            builder.HasOne(x => x.Encounter).WithMany(x => x.Transfers).HasForeignKey(x => x.ClinicalEncounterId);
            builder.HasOne(x => x.OriginClinic).WithMany().HasForeignKey(x => x.OriginClinicId);
            builder.HasOne(x => x.DestinationClinic).WithMany().HasForeignKey(x => x.DestinationClinicId);
            builder.HasOne(x => x.Ambulance).WithMany().HasForeignKey(x => x.AmbulanceId);
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.SentByStaffId);
            builder.HasOne<ClinicalStaff>().WithMany().HasForeignKey(x => x.ReceivedByStaffId);

            builder.HasIndex(x => new { x.ClinicalEncounterId, x.RequestedAtUtc });
            builder.HasIndex(x => new { x.DestinationClinicId, x.Status });
            builder.HasIndex(x => new { x.Status, x.RequestedAtUtc });

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_ClinicalTransfers_DifferentClinics",
                    Any(IsNull(origin), $"{Column(origin)} <> {Column(nameof(ClinicalTransfer.DestinationClinicId))}"));
                table.HasCheckConstraint("CK_ClinicalTransfers_Dates", All(
                    NotBefore(sentAt, requestedAt),
                    NotBefore(receivedAt, sentAt),
                    NotBefore(completedAt, receivedAt),
                    NotBefore(cancelledAt, requestedAt)));
                table.HasCheckConstraint("CK_ClinicalTransfers_Responsibles", All(
                    BothOrNeither(sentAt, nameof(ClinicalTransfer.SentByStaffId)),
                    BothOrNeither(receivedAt, nameof(ClinicalTransfer.ReceivedByStaffId))));
                table.HasCheckConstraint("CK_ClinicalTransfers_Lifecycle", Any(
                    All(Is(status, ClinicalTransferStatus.Requested), IsNull(sentAt), IsNull(receivedAt), IsNull(completedAt), IsNull(cancelledAt)),
                    All(Is(status, ClinicalTransferStatus.InTransit), IsNotNull(sentAt), IsNull(receivedAt), IsNull(completedAt), IsNull(cancelledAt)),
                    All(Is(status, ClinicalTransferStatus.Received), IsNotNull(sentAt), IsNotNull(receivedAt), IsNull(completedAt), IsNull(cancelledAt)),
                    All(Is(status, ClinicalTransferStatus.Completed), IsNotNull(sentAt), IsNotNull(receivedAt), IsNotNull(completedAt), IsNull(cancelledAt)),
                    All(Is(status, ClinicalTransferStatus.Cancelled), IsNotNull(cancelledAt), IsNull(receivedAt), IsNull(completedAt))));
            });
        }
    }
}
