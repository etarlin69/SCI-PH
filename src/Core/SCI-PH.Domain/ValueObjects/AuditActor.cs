using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Auditing;

namespace SCI_PH.Domain.ValueObjects
{
    // Who performed an audited action: a signed-in user, the system itself or a device.
    public sealed record AuditActor
    {
        private AuditActor(AuditActorType type, Guid? identityUserId, Guid? staffId, string? reference)
        {
            Type = type;
            IdentityUserId = identityUserId;
            StaffId = staffId;
            Reference = reference;
        }

        public AuditActorType Type { get; }
        public Guid? IdentityUserId { get; }
        public Guid? StaffId { get; }
        public string? Reference { get; }

        public static AuditActor User(Guid identityUserId, Guid? staffId = null) =>
            new(AuditActorType.User, Guard.Id(identityUserId, nameof(identityUserId)), Guard.OptionalId(staffId, nameof(staffId)), null);

        public static AuditActor System(string reference) =>
            new(AuditActorType.System, null, null, Guard.Text(reference, FieldLengths.ExternalReference, nameof(reference)));

        public static AuditActor Device(string reference) =>
            new(AuditActorType.Device, null, null, Guard.Text(reference, FieldLengths.ExternalReference, nameof(reference)));
    }
}
