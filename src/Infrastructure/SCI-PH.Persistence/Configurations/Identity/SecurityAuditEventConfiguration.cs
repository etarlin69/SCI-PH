using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Application.Common.Security;
using SCI_PH.Persistence.Identity.Models;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Identity
{
    // No foreign key to AspNetUsers on purpose: security history must outlive any account change.
    internal sealed class SecurityAuditEventConfiguration : IEntityTypeConfiguration<SecurityAuditEvent>
    {
        public void Configure(EntityTypeBuilder<SecurityAuditEvent> builder)
        {
            const string outcome = nameof(SecurityAuditEvent.Outcome);
            const string failureReason = nameof(SecurityAuditEvent.FailureReason);

            builder.HasKey(x => x.Id);
            builder.Property(x => x.IpAddress).HasMaxLength(SecurityFieldLengths.IpAddress);
            builder.Property(x => x.UserAgent).HasMaxLength(SecurityFieldLengths.UserAgent);

            builder.HasIndex(x => x.OccurredAtUtc);
            builder.HasIndex(x => new { x.UserId, x.OccurredAtUtc });
            builder.HasIndex(x => new { x.EventType, x.OccurredAtUtc });
            builder.HasIndex(x => new { x.DeviceCredentialId, x.OccurredAtUtc });

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_SecurityAuditEvents_Outcome", Any(
                    All(Is(outcome, SecurityAuditOutcome.Success), IsNull(failureReason)),
                    All(Is(outcome, SecurityAuditOutcome.Failure), IsNotNull(failureReason))));
                table.HasCheckConstraint("CK_SecurityAuditEvents_EventType_Enum",
                    In(nameof(SecurityAuditEvent.EventType), Enum.GetValues<SecurityAuditEventType>()));
                table.HasCheckConstraint("CK_SecurityAuditEvents_Outcome_Enum",
                    In(outcome, Enum.GetValues<SecurityAuditOutcome>()));
                table.HasCheckConstraint("CK_SecurityAuditEvents_FailureReason_Enum",
                    In(failureReason, Enum.GetValues<SecurityFailureReason>()));
            });
        }
    }
}
