using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.ValueObjects;

namespace SCI_PH.Domain.Entities.Auditing
{
    // Append-only audit record. Patient and encounter ids are plain references (no FK) so the log
    // never blocks or depends on the lifecycle of the audited rows.
    // Redact sensitive values before writing ChangesJson.
    public sealed class ClinicalAuditEntry
    {
        private ClinicalAuditEntry() { }

        public Guid Id { get; private set; }
        public AuditActionType Action { get; private set; }
        public string EntityName { get; private set; } = string.Empty;
        public string EntityId { get; private set; } = string.Empty;
        public AuditActorType ActorType { get; private set; }
        public Guid? ActorIdentityUserId { get; private set; }
        public Guid? ActorStaffId { get; private set; }
        // Device serial or system component name for non-user actors.
        public string? ActorReference { get; private set; }
        public AuditOutcome Outcome { get; private set; }
        public string? FailureReason { get; private set; }
        public Guid? PatientId { get; private set; }
        public Guid? ClinicalEncounterId { get; private set; }
        public string? CorrelationId { get; private set; }
        public string? ChangesJson { get; private set; }
        public DateTimeOffset OccurredAtUtc { get; private set; }

        public static ClinicalAuditEntry Success(
            AuditActor actor,
            AuditActionType action,
            string entityName,
            string entityId,
            DateTimeOffset occurredAtUtc,
            AuditContext? context = null) =>
            Create(actor, action, entityName, entityId, occurredAtUtc, AuditOutcome.Success, failureReason: null, context);

        public static ClinicalAuditEntry Failure(
            AuditActor actor,
            AuditActionType action,
            string entityName,
            string entityId,
            DateTimeOffset occurredAtUtc,
            string failureReason,
            AuditContext? context = null) =>
            Create(actor, action, entityName, entityId, occurredAtUtc, AuditOutcome.Failure,
                Guard.Text(failureReason, FieldLengths.ShortText, nameof(failureReason)), context);

        private static ClinicalAuditEntry Create(
            AuditActor actor,
            AuditActionType action,
            string entityName,
            string entityId,
            DateTimeOffset occurredAtUtc,
            AuditOutcome outcome,
            string? failureReason,
            AuditContext? context)
        {
            ArgumentNullException.ThrowIfNull(actor);
            context ??= new AuditContext();
            return new ClinicalAuditEntry
            {
                Id = Guid.NewGuid(),
                Action = Guard.Defined(action, nameof(action)),
                EntityName = Guard.Text(entityName, FieldLengths.AuditEntityName, nameof(entityName)),
                EntityId = Guard.Text(entityId, FieldLengths.AuditEntityId, nameof(entityId)),
                ActorType = actor.Type,
                ActorIdentityUserId = actor.IdentityUserId,
                ActorStaffId = actor.StaffId,
                ActorReference = actor.Reference,
                Outcome = outcome,
                FailureReason = failureReason,
                OccurredAtUtc = Guard.Utc(occurredAtUtc),
                PatientId = Guard.OptionalId(context.PatientId, nameof(context.PatientId)),
                ClinicalEncounterId = Guard.OptionalId(context.ClinicalEncounterId, nameof(context.ClinicalEncounterId)),
                CorrelationId = Guard.OptionalText(context.CorrelationId, FieldLengths.ExternalReference, nameof(context.CorrelationId)),
                ChangesJson = string.IsNullOrWhiteSpace(context.ChangesJson) ? null : context.ChangesJson
            };
        }
    }

    // Optional clinical context of an audited action.
    public sealed record AuditContext(
        Guid? PatientId = null,
        Guid? ClinicalEncounterId = null,
        string? CorrelationId = null,
        string? ChangesJson = null);
}
