using SCI_PH.Application.Common.Security;

namespace SCI_PH.Application.DTOs.Security
{
    // Never carries passwords, tokens, authorization headers or security stamps.
    // UserId is the affected account; ActorUserId is who performed an administrative action.
    public sealed record SecurityAuditRecord(
        SecurityAuditEventType EventType,
        SecurityAuditOutcome Outcome,
        Guid? UserId,
        SecurityFailureReason? FailureReason,
        ClientContext Client)
    {
        public Guid? ActorUserId { get; init; }
        public Guid? DeviceCredentialId { get; init; }

        public static SecurityAuditRecord Success(SecurityAuditEventType eventType, Guid? userId, ClientContext client) =>
            new(eventType, SecurityAuditOutcome.Success, userId, null, client);

        public static SecurityAuditRecord Failure(
            SecurityAuditEventType eventType,
            Guid? userId,
            SecurityFailureReason reason,
            ClientContext client) =>
            new(eventType, SecurityAuditOutcome.Failure, userId, reason, client);
    }
}
