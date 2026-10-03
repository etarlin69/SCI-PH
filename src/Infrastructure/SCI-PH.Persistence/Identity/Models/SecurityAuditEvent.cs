using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Persistence.Identity.Models
{
    // Append-only security event, separate from ClinicalAuditEntry. UserId has no foreign key so the
    // history survives any account change. Never stores passwords, tokens or authorization headers.
    public sealed class SecurityAuditEvent
    {
        private SecurityAuditEvent() { }

        public Guid Id { get; private set; }
        public Guid? UserId { get; private set; }
        // Administrator who performed the action, when different from the affected account.
        public Guid? ActorUserId { get; private set; }
        public Guid? DeviceCredentialId { get; private set; }
        public SecurityAuditEventType EventType { get; private set; }
        public SecurityAuditOutcome Outcome { get; private set; }
        public SecurityFailureReason? FailureReason { get; private set; }
        public DateTimeOffset OccurredAtUtc { get; private set; }
        public string? IpAddress { get; private set; }
        public string? UserAgent { get; private set; }

        public static SecurityAuditEvent From(SecurityAuditRecord record, DateTimeOffset occurredAtUtc) =>
            new()
            {
                Id = Guid.NewGuid(),
                UserId = record.UserId,
                ActorUserId = record.ActorUserId,
                DeviceCredentialId = record.DeviceCredentialId,
                EventType = record.EventType,
                Outcome = record.Outcome,
                FailureReason = record.FailureReason,
                OccurredAtUtc = occurredAtUtc.ToUniversalTime(),
                IpAddress = SecurityFieldLengths.Limit(record.Client.IpAddress, SecurityFieldLengths.IpAddress),
                UserAgent = SecurityFieldLengths.Limit(record.Client.UserAgent, SecurityFieldLengths.UserAgent)
            };
    }
}
