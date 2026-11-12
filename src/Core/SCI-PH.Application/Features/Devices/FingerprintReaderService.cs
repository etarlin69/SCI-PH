using System.Globalization;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Common.Biometrics;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Features.Devices
{
    // The fingerprint reader's side of a session (Device API, ADR-009). The reader is the authenticated DeviceCredential
    // (never a body field); it only sees its own sessions and only learns which session to capture for and until when.
    // It reports what the sensor matched; SCI-PH resolves the slot to a reference of THIS reader and records the attempt.
    // Nothing about the patient or the emergency is ever returned, and errors are deliberately generic: a reader cannot
    // tell a foreign session from a missing one, nor a known slot from an unknown one.
    public sealed class FingerprintReaderService
    {
        private const string NotFound = "The session was not found.";
        private const string NotAccepted = "The session does not accept this result.";

        private readonly IFingerprintSessionStore _sessions;
        private readonly IClinicalReadStore _reads;
        private readonly IClinicalWriteStore _writes;
        private readonly TimeProvider _time;

        public FingerprintReaderService(
            IFingerprintSessionStore sessions,
            IClinicalReadStore reads,
            IClinicalWriteStore writes,
            TimeProvider time)
        {
            _sessions = sessions;
            _reads = reads;
            _writes = writes;
            _time = time;
        }

        // The Pending session of this reader that has not expired, or null.
        public async Task<PendingFingerprintSession?> GetPendingAsync(Guid readerId, CancellationToken cancellationToken) =>
            await _sessions.FindPendingForReaderAsync(readerId, _time.GetUtcNow(), cancellationToken) is { } session
                ? new PendingFingerprintSession(session.Id, session.ExpiresAtUtc)
                : null;

        // The first valid result of a Pending, unexpired session of this reader resolves it (Matched, NoMatch or Failed) and
        // records exactly one IdentificationAttempt, in one transaction. The same result again (a lost response): accepted,
        // nothing written, whatever happened to the session since. Another result for a resolved session, or any result
        // for an expired or cancelled one: 409. A session of another reader, or none: 404.
        public async Task SubmitResultAsync(
            Guid readerId, string readerCode, Guid sessionId, SubmitFingerprintResultRequest request, CancellationToken cancellationToken)
        {
            var (outcome, slot) = Result(request);
            var resultHash = IdempotencyRequestHash.Compute(outcome.ToString(), slot?.ToString(CultureInfo.InvariantCulture));
            var session = await _sessions.FindAsync(sessionId, cancellationToken);
            if (session is null || session.DeviceCredentialId != readerId)
            {
                throw new ResourceNotFoundException(EmergencyFingerprintService.SessionEntityName, NotFound);
            }

            if (session.ResultHash is not null)
            {
                EnsureSameResult(session, resultHash);
                return;
            }

            var now = _time.GetUtcNow();
            if (session.Status != FingerprintSessionStatus.Pending)
            {
                throw new ResourceConflictException(NotAccepted);
            }

            if (session.IsDue(now))
            {
                await _sessions.ExpireDueAsync(new FingerprintSessionScope(SessionId: session.Id), now, cancellationToken);
                throw new ResourceConflictException(NotAccepted);
            }

            var context = new IdentificationAttemptContext(
                PerformedByStaffId: session.RequestedByStaffId,
                DeviceReference: readerCode,
                ClinicalEncounterId: session.ClinicalEncounterId,
                Provider: FingerprintReferences.Provider,
                ProviderRequestReference: session.Id.ToString("D"));
            var resolved = session with { ResolvedAtUtc = now, ResultHash = resultHash };
            IdentificationAttempt attempt;
            if (outcome == FingerprintCaptureOutcome.Match
                && await _reads.FindActiveFingerprintReferenceAsync(
                    FingerprintReferences.Provider, FingerprintReferences.SubjectReference(readerId, slot!.Value), cancellationToken) is { } reference)
            {
                resolved = resolved with { Status = FingerprintSessionStatus.Matched, MatchedBiometricReferenceId = reference.Id };
                attempt = IdentificationAttempt.Succeeded(
                    IdentificationMethod.Fingerprint, reference.PatientId, now, context, biometricReferenceId: reference.Id);
            }
            else
            {
                // A slot without an active reference of this reader is not trusted: no candidate, like no match.
                var (status, reason) = outcome switch
                {
                    FingerprintCaptureOutcome.LowQuality => (FingerprintSessionStatus.Failed, IdentificationFailureReason.LowQualityCapture),
                    FingerprintCaptureOutcome.SensorError => (FingerprintSessionStatus.Failed, IdentificationFailureReason.ProviderUnavailable),
                    _ => (FingerprintSessionStatus.NoMatch, IdentificationFailureReason.NoMatch)
                };
                resolved = resolved with { Status = status, FailureReason = reason };
                attempt = IdentificationAttempt.Failed(IdentificationMethod.Fingerprint, reason, now, context);
            }

            _sessions.Update(resolved, FingerprintSessionStatus.Pending);
            _writes.Add(attempt);
            try
            {
                await _writes.SaveChangesAsync(cancellationToken);
            }
            catch (ResourceConflictException exception)
            {
                // Another result, an expiry or a cancellation committed first: the same result is a replay.
                var current = await _sessions.FindAsync(session.Id, cancellationToken);
                if (current?.ResultHash == resultHash)
                {
                    return;
                }

                throw new ResourceConflictException(NotAccepted, exception);
            }
        }

        private static void EnsureSameResult(FingerprintSession session, string resultHash)
        {
            if (!string.Equals(session.ResultHash, resultHash, StringComparison.Ordinal))
            {
                throw new ResourceConflictException(NotAccepted);
            }
        }

        // slot: required (non-negative) for a match, absent otherwise. Its upper bound depends on the sensor and is not
        // fixed here.
        private static (FingerprintCaptureOutcome Outcome, int? Slot) Result(SubmitFingerprintResultRequest request)
        {
            if (!Enum.IsDefined(request.Outcome))
            {
                throw new RequestValidationException("outcome is not valid.", "outcome");
            }

            if (request.Outcome == FingerprintCaptureOutcome.Match)
            {
                return request.Slot is >= 0
                    ? (request.Outcome, request.Slot)
                    : throw new RequestValidationException("slot is required for a match and cannot be negative.", "slot");
            }

            return request.Slot is null
                ? (request.Outcome, null)
                : throw new RequestValidationException("slot is only allowed for a match.", "slot");
        }
    }
}
