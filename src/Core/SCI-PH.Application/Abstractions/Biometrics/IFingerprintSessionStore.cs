using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Abstractions.Biometrics
{
    // Fingerprint identification sessions: technical state that correlates ONE capture of ONE reader with ONE emergency
    // and ONE paramedic for a short time (FingerprintReferences.SessionTtl). It is not a Domain entity: the clinical facts
    // stay in IdentificationAttempt (each sensor result) and ClinicalEncounter.IdentifyPatient (the confirmation). It lives
    // in the clinical schema, so Add and Update only track the change and the next SaveChangesAsync of IClinicalWriteStore
    // commits it in the same transaction as the attempt, the identification, the audit entry and the receipt. Status is a
    // concurrency token: of two concurrent changes of the same session only one commits, the other surfaces as a
    // ResourceConflictException. The reader is a DeviceCredential of the identity schema: a scalar id, never a foreign key.
    public interface IFingerprintSessionStore
    {
        Task<FingerprintSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken);

        // The Pending session of this reader that has not expired at now (at most one: unique index).
        Task<FingerprintSession?> FindPendingForReaderAsync(Guid deviceCredentialId, DateTimeOffset now, CancellationToken cancellationToken);

        // Marks as Expired, at once and on its own (not in the pending transaction), the active sessions of the emergency,
        // of the reader or with this id whose expiry has passed at now. Expiry is evaluated when a session is used (no
        // background job); an expired session that is still Pending or Matched would otherwise keep its unique slot.
        Task ExpireDueAsync(FingerprintSessionScope scope, DateTimeOffset now, CancellationToken cancellationToken);

        void Add(FingerprintSession session);

        // Tracks the new state of a session read with FindAsync; the update only commits while the stored status is still
        // expectedStatus.
        void Update(FingerprintSession session, FingerprintSessionStatus expectedStatus);
    }

    // Which sessions ExpireDueAsync looks at; any null member is ignored.
    public sealed record FingerprintSessionScope(Guid? SessionId = null, Guid? ClinicalEncounterId = null, Guid? DeviceCredentialId = null);

    // ResultHash: SHA-256 of the result the reader sent (outcome and slot), so an identical retry is recognized without
    // keeping the slot in clear. MatchedBiometricReferenceId: the active reference the slot resolved to (Matched and later
    // Confirmed). FailureReason: why there is no candidate (NoMatch, Failed).
    public sealed record FingerprintSession(
        Guid Id,
        Guid ClinicalEncounterId,
        Guid DeviceCredentialId,
        Guid RequestedByStaffId,
        FingerprintSessionStatus Status,
        Guid? MatchedBiometricReferenceId,
        IdentificationFailureReason? FailureReason,
        string? ResultHash,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset? ResolvedAtUtc,
        DateTimeOffset? ConfirmedAtUtc)
    {
        public static IReadOnlyCollection<FingerprintSessionStatus> ActiveStatuses { get; } =
            [FingerprintSessionStatus.Pending, FingerprintSessionStatus.Matched];

        public bool IsActive => ActiveStatuses.Contains(Status);

        // Pending and Matched end at ExpiresAtUtc (inclusive), even if nobody marked them Expired yet.
        public bool IsDue(DateTimeOffset now) => IsActive && now >= ExpiresAtUtc;
    }

    // Pending: waiting for the reader. Matched: the reader's slot resolved to an active reference; waiting for the
    // paramedic. NoMatch: no candidate (no match, or a slot without an active reference). Failed: no usable capture (low
    // quality, sensor error). Expired, Cancelled and Confirmed end the session. Only Pending and Matched are active.
    public enum FingerprintSessionStatus
    {
        Pending = 1,
        Matched = 2,
        NoMatch = 3,
        Failed = 4,
        Expired = 5,
        Cancelled = 6,
        Confirmed = 7
    }
}
