using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Enums.Auditing;
using static SCI_PH.Persistence.Configurations.Sql;

namespace SCI_PH.Persistence.Configurations.Clinical.Auditing
{
    // No foreign keys on purpose: the audit log must accept entries about any row and never block
    // the lifecycle of the audited data.
    internal sealed class ClinicalAuditEntryConfiguration : IEntityTypeConfiguration<ClinicalAuditEntry>
    {
        public void Configure(EntityTypeBuilder<ClinicalAuditEntry> builder)
        {
            const string actorType = nameof(ClinicalAuditEntry.ActorType);
            const string actorUser = nameof(ClinicalAuditEntry.ActorIdentityUserId);
            const string actorReference = nameof(ClinicalAuditEntry.ActorReference);
            const string outcome = nameof(ClinicalAuditEntry.Outcome);
            const string failureReason = nameof(ClinicalAuditEntry.FailureReason);

            builder.HasKey(x => x.Id);
            builder.Property(x => x.EntityName).HasMaxLength(FieldLengths.AuditEntityName);
            builder.Property(x => x.EntityId).HasMaxLength(FieldLengths.AuditEntityId);
            builder.Property(x => x.ActorReference).HasMaxLength(FieldLengths.ExternalReference);
            builder.Property(x => x.FailureReason).HasMaxLength(FieldLengths.ShortText);
            builder.Property(x => x.CorrelationId).HasMaxLength(FieldLengths.ExternalReference);
            builder.Property(x => x.ChangesJson).HasColumnType("jsonb");

            builder.HasIndex(x => x.OccurredAtUtc);
            builder.HasIndex(x => new { x.EntityName, x.EntityId, x.OccurredAtUtc });
            builder.HasIndex(x => new { x.ActorIdentityUserId, x.OccurredAtUtc });
            builder.HasIndex(x => new { x.PatientId, x.OccurredAtUtc });

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_ClinicalAuditEntries_Actor", Any(
                    All(Is(actorType, AuditActorType.User), IsNotNull(actorUser), IsNull(actorReference)),
                    All(In(actorType, [AuditActorType.System, AuditActorType.Device]), IsNull(actorUser),
                        IsNull(nameof(ClinicalAuditEntry.ActorStaffId)), IsNotNull(actorReference))));
                table.HasCheckConstraint("CK_ClinicalAuditEntries_Outcome", Any(
                    All(Is(outcome, AuditOutcome.Success), IsNull(failureReason)),
                    All(Is(outcome, AuditOutcome.Failure), IsNotNull(failureReason))));
                table.HasCheckConstraint("CK_ClinicalAuditEntries_Entity", All(
                    NotBlank(nameof(ClinicalAuditEntry.EntityName)), NotBlank(nameof(ClinicalAuditEntry.EntityId))));
            });
        }
    }
}
