using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Persistence.Clinical.Biometrics
{
    // Technical persistence state, NOT a Clinical Domain entity (see IFingerprintEnrollmentStore): one enrollment of one
    // patient on one reader, asked by one staff member. Clinical schema, so it commits with the reference and the audit
    // entry it belongs to. DeviceCredentialId is a plain value (the reader lives in the identity schema). No template, image
    // or slot is stored: ResultHash is a SHA-256 of the reader's result and BiometricReferenceId the reference it created.
    public sealed class FingerprintEnrollmentSessionEntry
    {
        private FingerprintEnrollmentSessionEntry() { }

        public Guid Id { get; private set; }
        public Guid PatientId { get; private set; }
        public Guid DeviceCredentialId { get; private set; }
        public Guid RequestedByStaffId { get; private set; }
        public FingerprintEnrollmentStatus Status { get; private set; }
        public IdentificationFailureReason? FailureReason { get; private set; }
        public string? ResultHash { get; private set; }
        public Guid? BiometricReferenceId { get; private set; }
        public DateTimeOffset CreatedAtUtc { get; private set; }
        public DateTimeOffset ExpiresAtUtc { get; private set; }
        public DateTimeOffset? ResolvedAtUtc { get; private set; }

        public static FingerprintEnrollmentSessionEntry From(FingerprintEnrollment enrollment) =>
            new()
            {
                Id = enrollment.Id,
                PatientId = enrollment.PatientId,
                DeviceCredentialId = enrollment.DeviceCredentialId,
                RequestedByStaffId = enrollment.RequestedByStaffId,
                Status = enrollment.Status,
                FailureReason = enrollment.FailureReason,
                ResultHash = enrollment.ResultHash,
                BiometricReferenceId = enrollment.BiometricReferenceId,
                CreatedAtUtc = enrollment.CreatedAtUtc,
                ExpiresAtUtc = enrollment.ExpiresAtUtc,
                ResolvedAtUtc = enrollment.ResolvedAtUtc
            };

        public FingerprintEnrollment ToEnrollment() =>
            new(Id, PatientId, DeviceCredentialId, RequestedByStaffId, Status, FailureReason, ResultHash, BiometricReferenceId,
                CreatedAtUtc, ExpiresAtUtc, ResolvedAtUtc);
    }
}
