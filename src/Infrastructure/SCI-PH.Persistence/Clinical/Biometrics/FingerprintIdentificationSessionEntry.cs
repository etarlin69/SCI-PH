using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Persistence.Clinical.Biometrics
{
    // Technical persistence state, NOT a Clinical Domain entity (see IFingerprintSessionStore): the correlation of one
    // fingerprint capture with one emergency, one reader and one paramedic. It lives in the clinical schema so it commits
    // with the attempt, the identification and the audit entry it belongs to. DeviceCredentialId points to the identity
    // schema and is a plain value (no cross-context foreign key). No template, image, slot or subject reference is stored:
    // ResultHash is a SHA-256 of the reader's result and MatchedBiometricReferenceId the reference it resolved to.
    public sealed class FingerprintIdentificationSessionEntry
    {
        private FingerprintIdentificationSessionEntry() { }

        public Guid Id { get; private set; }
        public Guid ClinicalEncounterId { get; private set; }
        public Guid DeviceCredentialId { get; private set; }
        public Guid RequestedByStaffId { get; private set; }
        public FingerprintSessionStatus Status { get; private set; }
        public Guid? MatchedBiometricReferenceId { get; private set; }
        public IdentificationFailureReason? FailureReason { get; private set; }
        public string? ResultHash { get; private set; }
        public DateTimeOffset CreatedAtUtc { get; private set; }
        public DateTimeOffset ExpiresAtUtc { get; private set; }
        public DateTimeOffset? ResolvedAtUtc { get; private set; }
        public DateTimeOffset? ConfirmedAtUtc { get; private set; }

        public static FingerprintIdentificationSessionEntry From(FingerprintSession session) =>
            new()
            {
                Id = session.Id,
                ClinicalEncounterId = session.ClinicalEncounterId,
                DeviceCredentialId = session.DeviceCredentialId,
                RequestedByStaffId = session.RequestedByStaffId,
                Status = session.Status,
                MatchedBiometricReferenceId = session.MatchedBiometricReferenceId,
                FailureReason = session.FailureReason,
                ResultHash = session.ResultHash,
                CreatedAtUtc = session.CreatedAtUtc,
                ExpiresAtUtc = session.ExpiresAtUtc,
                ResolvedAtUtc = session.ResolvedAtUtc,
                ConfirmedAtUtc = session.ConfirmedAtUtc
            };

        public FingerprintSession ToSession() =>
            new(Id, ClinicalEncounterId, DeviceCredentialId, RequestedByStaffId, Status, MatchedBiometricReferenceId,
                FailureReason, ResultHash, CreatedAtUtc, ExpiresAtUtc, ResolvedAtUtc, ConfirmedAtUtc);
    }
}
