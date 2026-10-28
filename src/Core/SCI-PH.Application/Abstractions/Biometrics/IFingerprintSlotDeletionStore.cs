namespace SCI_PH.Application.Abstractions.Biometrics
{
    // Physical cleanup of a revoked fingerprint (ADR-011): ONE job per revoked PatientBiometricReference, asking ITS reader to
    // delete the template from the sensor slot. Technical state, not a Domain entity: the security fact is the revocation,
    // committed with the job, and the job is only cleanup. Same contract as IFingerprintEnrollmentStore: Add and Update only
    // track, the next SaveChangesAsync of IClinicalWriteStore commits; the reader is a scalar DeviceCredentialId (identity
    // schema, no foreign key). The slot is never stored here: it is read from the reference's SubjectReference.
    public interface IFingerprintSlotDeletionStore
    {
        Task<FingerprintSlotDeletionTarget?> FindAsync(Guid deletionId, CancellationToken cancellationToken);

        // The oldest Pending job of this reader (jobs do not expire).
        Task<FingerprintSlotDeletionTarget?> FindOldestPendingForReaderAsync(Guid deviceCredentialId, CancellationToken cancellationToken);

        // The jobs of these references (at most one each), by reference id.
        Task<IReadOnlyDictionary<Guid, FingerprintSlotDeletion>> ListByReferencesAsync(
            IReadOnlyCollection<Guid> biometricReferenceIds, CancellationToken cancellationToken);

        // Whether this reader still has to delete the slot of this subject reference: its template is physically there, so the
        // slot cannot hold a new enrollment yet.
        Task<bool> HasPendingForSubjectAsync(Guid deviceCredentialId, string subjectReference, CancellationToken cancellationToken);

        void Add(FingerprintSlotDeletion deletion);

        // Tracks the new state of a job read with FindAsync; it only commits while the stored status AND last attempt are
        // still the expected ones (two reports of the reader cannot both apply).
        void Update(FingerprintSlotDeletion deletion, FingerprintSlotDeletionStatus expectedStatus, Guid? expectedLastAttemptId);
    }

    // A job with what its readers need: the reference's patient (for the audit entry only, never sent to the reader) and its
    // subject reference (to derive the slot, never sent either).
    public sealed record FingerprintSlotDeletionTarget(FingerprintSlotDeletion Deletion, Guid PatientId, string SubjectReference);

    // Attempts: physical attempts the reader reported (each with its own attemptId). LastAttemptId and ResultHash (SHA-256 of
    // the attempt id and outcome) recognize a repeated report of the last attempt. LastFailure: a category, never a message
    // from the hardware.
    public sealed record FingerprintSlotDeletion(
        Guid Id,
        Guid BiometricReferenceId,
        Guid DeviceCredentialId,
        FingerprintSlotDeletionStatus Status,
        FingerprintSlotDeletionOutcome? Outcome,
        int Attempts,
        Guid? LastAttemptId,
        string? ResultHash,
        DateTimeOffset? LastAttemptAtUtc,
        FingerprintSlotDeletionFailure? LastFailure,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? CompletedAtUtc)
    {
        public bool IsPending => Status == FingerprintSlotDeletionStatus.Pending;

        public static FingerprintSlotDeletion Request(Guid biometricReferenceId, Guid deviceCredentialId, DateTimeOffset now) =>
            new(Guid.NewGuid(), biometricReferenceId, deviceCredentialId, FingerprintSlotDeletionStatus.Pending, Outcome: null,
                Attempts: 0, LastAttemptId: null, ResultHash: null, LastAttemptAtUtc: null, LastFailure: null,
                CreatedAtUtc: now, CompletedAtUtc: null);
    }

    // Pending until the reader reports the slot gone; a sensor error keeps it Pending (retried). No expiry and no Abandoned:
    // the reference is already unusable, cleanup can still be useful days later.
    public enum FingerprintSlotDeletionStatus
    {
        Pending = 1,
        Completed = 2
    }

    // Both mean the slot no longer holds the template.
    public enum FingerprintSlotDeletionOutcome
    {
        Deleted = 1,
        NotFound = 2
    }

    public enum FingerprintSlotDeletionFailure
    {
        SensorError = 1
    }
}
