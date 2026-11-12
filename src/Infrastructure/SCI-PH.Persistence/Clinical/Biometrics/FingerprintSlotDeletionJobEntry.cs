using SCI_PH.Application.Abstractions.Biometrics;

namespace SCI_PH.Persistence.Clinical.Biometrics
{
    // Technical persistence state, NOT a Clinical Domain entity (see IFingerprintSlotDeletionStore): the deletion of one
    // revoked reference's template from its reader. Clinical schema, so it commits with the revocation and its audit entry.
    // DeviceCredentialId is a plain value (the reader lives in the identity schema). No patient, slot, subject reference,
    // template or image is stored: the reference (foreign key) holds the subject, and ResultHash is a SHA-256.
    public sealed class FingerprintSlotDeletionJobEntry
    {
        private FingerprintSlotDeletionJobEntry() { }

        public Guid Id { get; private set; }
        public Guid BiometricReferenceId { get; private set; }
        public Guid DeviceCredentialId { get; private set; }
        public FingerprintSlotDeletionStatus Status { get; private set; }
        public FingerprintSlotDeletionOutcome? Outcome { get; private set; }
        public int Attempts { get; private set; }
        public Guid? LastAttemptId { get; private set; }
        public string? ResultHash { get; private set; }
        public DateTimeOffset? LastAttemptAtUtc { get; private set; }
        public FingerprintSlotDeletionFailure? LastFailure { get; private set; }
        public DateTimeOffset CreatedAtUtc { get; private set; }
        public DateTimeOffset? CompletedAtUtc { get; private set; }

        public static FingerprintSlotDeletionJobEntry From(FingerprintSlotDeletion deletion) =>
            new()
            {
                Id = deletion.Id,
                BiometricReferenceId = deletion.BiometricReferenceId,
                DeviceCredentialId = deletion.DeviceCredentialId,
                Status = deletion.Status,
                Outcome = deletion.Outcome,
                Attempts = deletion.Attempts,
                LastAttemptId = deletion.LastAttemptId,
                ResultHash = deletion.ResultHash,
                LastAttemptAtUtc = deletion.LastAttemptAtUtc,
                LastFailure = deletion.LastFailure,
                CreatedAtUtc = deletion.CreatedAtUtc,
                CompletedAtUtc = deletion.CompletedAtUtc
            };

        public FingerprintSlotDeletion ToDeletion() =>
            new(Id, BiometricReferenceId, DeviceCredentialId, Status, Outcome, Attempts, LastAttemptId, ResultHash, LastAttemptAtUtc,
                LastFailure, CreatedAtUtc, CompletedAtUtc);
    }
}
