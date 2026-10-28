using System.Text.Json;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Common.Biometrics;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.ValueObjects;

namespace SCI_PH.Application.Features.Devices
{
    // The reader's side of the physical cleanup of a revoked fingerprint (Device API, ADR-011). The reader is the
    // authenticated DeviceCredential; it only sees its own jobs and learns only which job and which slot of its sensor, never
    // the patient or the reference. The reference is already Revoked: nothing here affects identification. Errors are
    // generic: a reader cannot tell a foreign job from a missing one.
    public sealed class FingerprintSlotDeletionReaderService
    {
        private const string NotFound = "The deletion was not found.";
        private const string NotAccepted = "The deletion does not accept this result.";

        private readonly IFingerprintSlotDeletionStore _deletions;
        private readonly IClinicalWriteStore _writes;
        private readonly TimeProvider _time;

        public FingerprintSlotDeletionReaderService(IFingerprintSlotDeletionStore deletions, IClinicalWriteStore writes, TimeProvider time)
        {
            _deletions = deletions;
            _writes = writes;
            _time = time;
        }

        // The oldest Pending job of this reader with the slot to delete, taken from the reference (never from the reader), or
        // null.
        public async Task<PendingFingerprintSlotDeletion?> GetPendingAsync(Guid readerId, CancellationToken cancellationToken)
        {
            if (await _deletions.FindOldestPendingForReaderAsync(readerId, cancellationToken) is not { } target)
            {
                return null;
            }

            // A job is only queued for a reference of this format and of this reader (FingerprintRevocationService).
            return FingerprintReferences.TryParse(target.SubjectReference, out var owner, out var slot) && owner == readerId
                ? new PendingFingerprintSlotDeletion(target.Deletion.Id, slot)
                : throw new InvalidOperationException("A fingerprint slot deletion does not match its reference.");
        }

        // One report per PHYSICAL attempt, identified by the reader's attemptId:
        // - the same attempt again (a lost 204): accepted only with the same outcome, nothing written; otherwise 409;
        // - a new attempt on a Pending job: Deleted or NotFound completes it (both mean the slot is gone); SensorError keeps
        //   it Pending and counts one more attempt (the first sensor error of the job is audited, later ones are not);
        // - a new attempt on a Completed job: 409 (its outcome never changes).
        // A job of another reader, or none: 404. Two reports at once: one commits, the other is decided again on it.
        public async Task SubmitResultAsync(
            Guid readerId, string readerCode, Guid deletionId, SubmitFingerprintSlotDeletionResultRequest request, CancellationToken cancellationToken)
        {
            var (attemptId, outcome) = Result(request);
            var resultHash = IdempotencyRequestHash.Compute(attemptId.ToString("D"), outcome.ToString());
            var target = await _deletions.FindAsync(deletionId, cancellationToken);
            if (target is null || target.Deletion.DeviceCredentialId != readerId)
            {
                throw new ResourceNotFoundException(FingerprintRevocationService.DeletionEntityName, NotFound);
            }

            for (var attempt = 1; ; attempt++)
            {
                var deletion = target.Deletion;
                if (deletion.LastAttemptId == attemptId)
                {
                    EnsureSameResult(deletion, resultHash);
                    return;
                }

                if (!deletion.IsPending)
                {
                    throw new ResourceConflictException(NotAccepted);
                }

                var now = _time.GetUtcNow();
                var reported = deletion with
                {
                    Attempts = deletion.Attempts + 1,
                    LastAttemptId = attemptId,
                    ResultHash = resultHash,
                    LastAttemptAtUtc = now
                };
                var device = AuditActor.Device(readerCode);
                if (outcome == FingerprintSlotDeletionResultOutcome.SensorError)
                {
                    reported = reported with { LastFailure = FingerprintSlotDeletionFailure.SensorError };
                    if (deletion.LastFailure is null)
                    {
                        _writes.Add(ClinicalAuditEntry.Success(
                            device, AuditActionType.Update, FingerprintRevocationService.DeletionEntityName, deletion.Id.ToString(), now,
                            new AuditContext(PatientId: target.PatientId, ChangesJson: JsonSerializer.Serialize(new
                            {
                                failure = outcome.ToString()
                            }))));
                    }
                }
                else
                {
                    reported = reported with
                    {
                        Status = FingerprintSlotDeletionStatus.Completed,
                        Outcome = outcome == FingerprintSlotDeletionResultOutcome.Deleted
                            ? FingerprintSlotDeletionOutcome.Deleted
                            : FingerprintSlotDeletionOutcome.NotFound,
                        CompletedAtUtc = now
                    };
                    _writes.Add(ClinicalAuditEntry.Success(
                        device, AuditActionType.Update, FingerprintRevocationService.DeletionEntityName, deletion.Id.ToString(), now,
                        new AuditContext(PatientId: target.PatientId, ChangesJson: JsonSerializer.Serialize(new
                        {
                            status = new[] { deletion.Status.ToString(), reported.Status.ToString() },
                            outcome = reported.Outcome.ToString()
                        }))));
                }

                _deletions.Update(reported, deletion.Status, deletion.LastAttemptId);
                try
                {
                    await _writes.SaveChangesAsync(cancellationToken);
                    return;
                }
                catch (ResourceConflictException exception)
                {
                    // Another report committed first; nothing of this one was written. Decide again on what it left: the
                    // same attempt is a replay, a completed job rejects, a still Pending one takes this attempt.
                    target = await _deletions.FindAsync(deletionId, cancellationToken)
                        ?? throw new InvalidOperationException("A fingerprint slot deletion disappeared.");
                    if (attempt >= 2 && target.Deletion.LastAttemptId != attemptId)
                    {
                        throw new ResourceConflictException(NotAccepted, exception);
                    }
                }
            }
        }

        private static void EnsureSameResult(FingerprintSlotDeletion deletion, string resultHash)
        {
            if (!string.Equals(deletion.ResultHash, resultHash, StringComparison.Ordinal))
            {
                throw new ResourceConflictException(NotAccepted);
            }
        }

        private static (Guid AttemptId, FingerprintSlotDeletionResultOutcome Outcome) Result(SubmitFingerprintSlotDeletionResultRequest request)
        {
            if (request.AttemptId == Guid.Empty)
            {
                throw new RequestValidationException("attemptId is required.", "attemptId");
            }

            return Enum.IsDefined(request.Outcome)
                ? (request.AttemptId, request.Outcome)
                : throw new RequestValidationException("outcome is not valid.", "outcome");
        }
    }
}
