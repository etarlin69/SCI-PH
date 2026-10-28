using System.Text.Json;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.ValueObjects;

namespace SCI_PH.Application.Features.Clinical
{
    // Durable cleanup of revoked face templates (ADR-012, PR 2). A revocation commits a Pending job with it; this service
    // asks the CONFIGURED provider to delete the template, right after the revocation and then on every worker pass until it
    // succeeds. Deleted and NotFound both complete the job (the template is gone); a provider failure counts one more attempt
    // and is retried after a growing delay; a reference of another provider is never sent to this one. At-least-once: two
    // workers may try the same job, the status and attempts concurrency tokens let only one result apply, and only the final
    // completion is audited. Security never depends on this: the revoked reference is already unusable.
    public sealed class FaceTemplateCleanupService
    {
        public const string AuditReference = "face-template-cleanup";

        // Shown as needing attention from this many failed attempts; retries go on.
        public const int AttentionAttempts = 5;

        private static readonly TimeSpan[] RetryDelays =
            [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)];

        private readonly IFaceTemplateDeletionStore _jobs;
        private readonly IClinicalWriteStore _writes;
        private readonly IFaceVerificationService _faces;
        private readonly TimeProvider _time;

        public FaceTemplateCleanupService(IFaceTemplateDeletionStore jobs, IClinicalWriteStore writes, IFaceVerificationService faces, TimeProvider time)
        {
            _jobs = jobs;
            _writes = writes;
            _faces = faces;
            _time = time;
        }

        // Wait before retrying a job that failed `attempts` times: 1, 2, 3, 5, then 10 minutes.
        public static TimeSpan RetryDelay(int attempts) => RetryDelays[Math.Clamp(attempts - 1, 0, RetryDelays.Length - 1)];

        public static bool IsDue(FaceTemplateDeletion deletion, DateTimeOffset now) =>
            deletion.IsPending && (deletion.LastAttemptAtUtc is not { } last || now >= last + RetryDelay(deletion.Attempts));

        // One worker pass: the due Pending jobs (at most `limit`). Returns how many it completed.
        public async Task<int> RunOnceAsync(int limit, CancellationToken cancellationToken)
        {
            var now = _time.GetUtcNow();
            var completed = 0;
            foreach (var target in await _jobs.ListPendingAsync(limit, cancellationToken))
            {
                if (IsDue(target.Deletion, now) && await ProcessAsync(target, cancellationToken))
                {
                    completed++;
                }
            }

            return completed;
        }

        // The immediate attempt after a revocation. False when the job stays Pending (or another worker completed it).
        public async Task<bool> TryCompleteAsync(Guid deletionId, CancellationToken cancellationToken) =>
            await _jobs.FindAsync(deletionId, cancellationToken) is { Deletion.IsPending: true } target
            && await ProcessAsync(target, cancellationToken);

        private async Task<bool> ProcessAsync(FaceTemplateDeletionTarget target, CancellationToken cancellationToken)
        {
            var deletion = target.Deletion;
            var now = _time.GetUtcNow();
            FaceTemplateDeletion next;
            ClinicalAuditEntry? audit = null;
            if (target.Modality != BiometricModality.Face || !string.Equals(target.Provider, _faces.Provider, StringComparison.Ordinal))
            {
                // Never interpret another provider's subject reference.
                next = Failed(deletion, FaceTemplateDeletionFailure.ProviderMismatch, now);
            }
            else
            {
                FaceDeletionOutcome? outcome;
                try
                {
                    outcome = await _faces.DeleteAsync(target.SubjectReference, cancellationToken);
                }
                catch (ExternalServiceUnavailableException)
                {
                    outcome = null;
                }

                if (outcome is not { } done)
                {
                    next = Failed(deletion, FaceTemplateDeletionFailure.ProviderUnavailable, now);
                }
                else
                {
                    var result = done == FaceDeletionOutcome.Deleted ? FaceTemplateDeletionOutcome.Deleted : FaceTemplateDeletionOutcome.NotFound;
                    next = deletion with { Status = FaceTemplateDeletionStatus.Completed, Outcome = result, CompletedAtUtc = now, LastAttemptAtUtc = now };
                    audit = ClinicalAuditEntry.Success(
                        AuditActor.System(AuditReference), AuditActionType.Update, nameof(PatientBiometricReference),
                        deletion.BiometricReferenceId.ToString(), now,
                        new AuditContext(PatientId: target.PatientId, ChangesJson: JsonSerializer.Serialize(new { cleanup = result.ToString() })));
                }
            }

            _jobs.Update(next, deletion.Status, deletion.Attempts);
            if (audit is not null)
            {
                _writes.Add(audit);
            }

            try
            {
                await _writes.SaveChangesAsync(cancellationToken);
                return next.Status == FaceTemplateDeletionStatus.Completed;
            }
            catch (ResourceConflictException)
            {
                // Another worker applied its result first; the job converges on that one.
                return false;
            }
        }

        private static FaceTemplateDeletion Failed(FaceTemplateDeletion deletion, FaceTemplateDeletionFailure failure, DateTimeOffset now) =>
            deletion with { Attempts = deletion.Attempts + 1, LastAttemptAtUtc = now, LastFailure = failure };
    }
}
