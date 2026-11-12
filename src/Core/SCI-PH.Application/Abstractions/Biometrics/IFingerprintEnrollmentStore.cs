using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Abstractions.Biometrics
{
    // Fingerprint enrollment sessions (ADR-010): technical state that correlates ONE enrollment on ONE reader with ONE
    // patient and the staff member who asked for it, for FingerprintReferences.EnrollmentTtl. Not a Domain entity: the
    // clinical fact is the PatientBiometricReference it creates. Same contract as IFingerprintSessionStore: Add and Update
    // only track, the next SaveChangesAsync of IClinicalWriteStore commits with the reference, the audit entry and the
    // receipt; Status is a concurrency token; the reader is a scalar DeviceCredentialId (identity schema, no foreign key).
    public interface IFingerprintEnrollmentStore
    {
        Task<FingerprintEnrollment?> FindAsync(Guid enrollmentId, CancellationToken cancellationToken);

        // The Pending enrollment of this reader that has not expired at now (at most one: unique index).
        Task<FingerprintEnrollment?> FindPendingForReaderAsync(Guid deviceCredentialId, DateTimeOffset now, CancellationToken cancellationToken);

        // Marks as Expired, at once and on its own, the Pending enrollments of the patient, of the reader or with this id
        // whose expiry has passed at now (no background job; an expired Pending one would keep its unique slot).
        Task ExpireDueAsync(FingerprintEnrollmentScope scope, DateTimeOffset now, CancellationToken cancellationToken);

        void Add(FingerprintEnrollment enrollment);

        // Tracks the new state of an enrollment read with FindAsync; it only commits while the stored status is still
        // expectedStatus.
        void Update(FingerprintEnrollment enrollment, FingerprintEnrollmentStatus expectedStatus);
    }

    // Which enrollments ExpireDueAsync looks at; any null member is ignored.
    public sealed record FingerprintEnrollmentScope(Guid? EnrollmentId = null, Guid? PatientId = null, Guid? DeviceCredentialId = null);

    // ResultHash: SHA-256 of the reader's result (outcome and slot), so an identical retry is recognized without keeping the
    // slot in clear (an enrolled slot is in the created reference). FailureReason: why it Failed.
    public sealed record FingerprintEnrollment(
        Guid Id,
        Guid PatientId,
        Guid DeviceCredentialId,
        Guid RequestedByStaffId,
        FingerprintEnrollmentStatus Status,
        IdentificationFailureReason? FailureReason,
        string? ResultHash,
        Guid? BiometricReferenceId,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset? ResolvedAtUtc)
    {
        public bool IsPending => Status == FingerprintEnrollmentStatus.Pending;

        // Pending ends at ExpiresAtUtc (inclusive), even if nobody marked it Expired yet.
        public bool IsDue(DateTimeOffset now) => IsPending && now >= ExpiresAtUtc;
    }

    // Pending: waiting for the reader (the only active status: the backend cannot know when the sensor captures).
    // Enrolled: the reference exists. Failed: no usable enrollment (failure, low quality, sensor error); a new attempt is
    // a new session. Expired and Cancelled end it without a reference.
    public enum FingerprintEnrollmentStatus
    {
        Pending = 1,
        Enrolled = 2,
        Failed = 3,
        Expired = 4,
        Cancelled = 5
    }
}
