using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Abstractions.Biometrics
{
    // Durable cleanup of a revoked face reference's provider template (ADR-012, PR 2): ONE job per revoked reference,
    // committed in the same transaction as the revocation. Technical state, not a Domain entity: the security fact is the
    // revocation; the job only removes the template from the provider's store. Same contract as the other technical stores:
    // Add and Update only track, the next SaveChangesAsync of IClinicalWriteStore commits. The provider, subject reference
    // and patient are never stored here: they are read from the reference.
    public interface IFaceTemplateDeletionStore
    {
        Task<FaceTemplateDeletionTarget?> FindAsync(Guid deletionId, CancellationToken cancellationToken);

        // Pending jobs, oldest first (whether each is due for a retry is decided by FaceTemplateCleanupService).
        Task<IReadOnlyList<FaceTemplateDeletionTarget>> ListPendingAsync(int limit, CancellationToken cancellationToken);

        void Add(FaceTemplateDeletion deletion);

        // Reconciliation (report only): the face references of `provider` whose template must still exist in its store,
        // Active ones and Revoked ones whose cleanup is still Pending.
        Task<IReadOnlyList<RetainedFaceTemplate>> ListRetainedAsync(string provider, CancellationToken cancellationToken);

        // Tracks the new state of a job read with FindAsync or ListPendingAsync; it only commits while the stored status AND
        // attempts are still the expected ones, so of two workers on the same job only one applies its result.
        void Update(FaceTemplateDeletion deletion, FaceTemplateDeletionStatus expectedStatus, int expectedAttempts);
    }

    // A job with what the cleanup needs from its reference (never written back): the reference's patient (audit only),
    // modality, provider and opaque subject reference.
    public sealed record FaceTemplateDeletionTarget(
        FaceTemplateDeletion Deletion, Guid PatientId, BiometricModality Modality, string Provider, string SubjectReference);

    public sealed record RetainedFaceTemplate(string SubjectReference, bool Active);

    // Attempts: failed cleanup attempts so far (they drive the retry delay; they never affect security).
    public sealed record FaceTemplateDeletion(
        Guid Id,
        Guid BiometricReferenceId,
        FaceTemplateDeletionStatus Status,
        FaceTemplateDeletionOutcome? Outcome,
        int Attempts,
        DateTimeOffset? LastAttemptAtUtc,
        FaceTemplateDeletionFailure? LastFailure,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? CompletedAtUtc)
    {
        public bool IsPending => Status == FaceTemplateDeletionStatus.Pending;

        public static FaceTemplateDeletion Request(Guid biometricReferenceId, DateTimeOffset now) =>
            new(Guid.NewGuid(), biometricReferenceId, FaceTemplateDeletionStatus.Pending, Outcome: null, Attempts: 0,
                LastAttemptAtUtc: null, LastFailure: null, CreatedAtUtc: now, CompletedAtUtc: null);
    }

    // Pending until the provider confirms the template is gone. No expiry and no abandoned state: the reference is already
    // unusable, the cleanup keeps being retried.
    public enum FaceTemplateDeletionStatus
    {
        Pending = 1,
        Completed = 2
    }

    // Both mean the template no longer exists.
    public enum FaceTemplateDeletionOutcome
    {
        Deleted = 1,
        NotFound = 2
    }

    // ProviderUnavailable: the provider or its store failed (retried). ProviderMismatch: the reference belongs to another
    // provider than the configured one; nothing is ever sent to it (needs attention).
    public enum FaceTemplateDeletionFailure
    {
        ProviderUnavailable = 1,
        ProviderMismatch = 2
    }
}
