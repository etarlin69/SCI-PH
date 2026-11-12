using SCI_PH.Application.Abstractions.Biometrics;

namespace SCI_PH.Persistence.Clinical.Biometrics
{
    // Technical persistence state, NOT a Clinical Domain entity (see IFaceTemplateDeletionStore): the cleanup of one revoked
    // face reference's provider template. Clinical schema, so it commits with the revocation. No patient, provider, subject
    // reference, template or image: the reference (foreign key) holds them.
    public sealed class FaceTemplateDeletionJobEntry
    {
        private FaceTemplateDeletionJobEntry() { }

        public Guid Id { get; private set; }
        public Guid BiometricReferenceId { get; private set; }
        public FaceTemplateDeletionStatus Status { get; private set; }
        public FaceTemplateDeletionOutcome? Outcome { get; private set; }
        public int Attempts { get; private set; }
        public DateTimeOffset? LastAttemptAtUtc { get; private set; }
        public FaceTemplateDeletionFailure? LastFailure { get; private set; }
        public DateTimeOffset CreatedAtUtc { get; private set; }
        public DateTimeOffset? CompletedAtUtc { get; private set; }

        public static FaceTemplateDeletionJobEntry From(FaceTemplateDeletion deletion) =>
            new()
            {
                Id = deletion.Id,
                BiometricReferenceId = deletion.BiometricReferenceId,
                Status = deletion.Status,
                Outcome = deletion.Outcome,
                Attempts = deletion.Attempts,
                LastAttemptAtUtc = deletion.LastAttemptAtUtc,
                LastFailure = deletion.LastFailure,
                CreatedAtUtc = deletion.CreatedAtUtc,
                CompletedAtUtc = deletion.CompletedAtUtc
            };

        public FaceTemplateDeletion ToDeletion() =>
            new(Id, BiometricReferenceId, Status, Outcome, Attempts, LastAttemptAtUtc, LastFailure, CreatedAtUtc, CompletedAtUtc);
    }
}
