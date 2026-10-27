using System.Globalization;
using System.Text.Json;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Common.Biometrics;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.ValueObjects;

namespace SCI_PH.Application.Features.Devices
{
    // The reader's side of an enrollment (Device API, ADR-010). The reader is the authenticated DeviceCredential; it only
    // sees its own enrollments and learns only which one to do and until when, never the patient. It stores the template
    // on its sensor FIRST, in a slot it chose, and then reports the slot: SCI-PH creates the reference only if the slot is
    // not an active reference of that reader already. The reader keeps the slot only after a 204; after a terminal
    // rejection (404, 409) it must delete what it just stored. Errors are generic.
    public sealed class FingerprintEnrollmentReaderService
    {
        private const string NotFound = "The enrollment was not found.";
        private const string NotAccepted = "The enrollment does not accept this result.";

        private readonly IFingerprintEnrollmentStore _enrollments;
        private readonly IClinicalReadStore _reads;
        private readonly IClinicalWriteStore _writes;
        private readonly TimeProvider _time;

        public FingerprintEnrollmentReaderService(
            IFingerprintEnrollmentStore enrollments,
            IClinicalReadStore reads,
            IClinicalWriteStore writes,
            TimeProvider time)
        {
            _enrollments = enrollments;
            _reads = reads;
            _writes = writes;
            _time = time;
        }

        // The Pending, unexpired enrollment of this reader, or null.
        public async Task<PendingFingerprintEnrollment?> GetPendingAsync(Guid readerId, CancellationToken cancellationToken) =>
            await _enrollments.FindPendingForReaderAsync(readerId, _time.GetUtcNow(), cancellationToken) is { } enrollment
                ? new PendingFingerprintEnrollment(enrollment.Id, enrollment.ExpiresAtUtc)
                : null;

        // The first valid result of a Pending, unexpired enrollment of this reader resolves it, in one transaction: Enrolled
        // creates the PatientBiometricReference (registered by the staff member who opened the enrollment) and its audit
        // entry; a failure records the reason. The same result again (a lost 204): accepted, nothing written. Another result
        // after that, or any result for an expired or cancelled enrollment: 409. A slot that is already an active reference of
        // this reader, or a patient who already has an active fingerprint on it: 409 and the enrollment stays Pending (the
        // reader discards its slot). An enrollment of another reader, or none: 404.
        public async Task SubmitResultAsync(
            Guid readerId, string readerCode, Guid enrollmentId, SubmitFingerprintEnrollmentResultRequest request, CancellationToken cancellationToken)
        {
            var (outcome, slot) = Result(request);
            var resultHash = IdempotencyRequestHash.Compute(outcome.ToString(), slot?.ToString(CultureInfo.InvariantCulture));
            var enrollment = await _enrollments.FindAsync(enrollmentId, cancellationToken);
            if (enrollment is null || enrollment.DeviceCredentialId != readerId)
            {
                throw new ResourceNotFoundException(FingerprintEnrollmentService.EnrollmentEntityName, NotFound);
            }

            if (enrollment.ResultHash is not null)
            {
                EnsureSameResult(enrollment, resultHash);
                return;
            }

            var now = _time.GetUtcNow();
            if (!enrollment.IsPending)
            {
                throw new ResourceConflictException(NotAccepted);
            }

            if (enrollment.IsDue(now))
            {
                await _enrollments.ExpireDueAsync(new FingerprintEnrollmentScope(EnrollmentId: enrollment.Id), now, cancellationToken);
                throw new ResourceConflictException(NotAccepted);
            }

            var device = AuditActor.Device(readerCode);
            var resolved = enrollment with { ResolvedAtUtc = now, ResultHash = resultHash };
            if (outcome == FingerprintEnrollmentOutcome.Enrolled)
            {
                var subjectReference = FingerprintReferences.SubjectReference(readerId, slot!.Value);
                if (await _reads.FindActiveFingerprintReferenceAsync(FingerprintReferences.Provider, subjectReference, cancellationToken) is not null
                    || await _reads.HasActiveFingerprintReferenceAsync(
                        enrollment.PatientId, FingerprintReferences.Provider, FingerprintReferences.ReaderPrefix(readerId), cancellationToken))
                {
                    throw new ResourceConflictException(NotAccepted);
                }

                var reference = PatientBiometricReference.Register(
                    enrollment.PatientId, BiometricModality.Fingerprint, FingerprintReferences.Provider, subjectReference,
                    registeredByStaffId: enrollment.RequestedByStaffId);
                resolved = resolved with { Status = FingerprintEnrollmentStatus.Enrolled, BiometricReferenceId = reference.Id };
                _writes.Add(reference);
                _writes.Add(ClinicalAuditEntry.Success(
                    device, AuditActionType.Create, nameof(PatientBiometricReference), reference.Id.ToString(), now,
                    new AuditContext(PatientId: enrollment.PatientId, ChangesJson: JsonSerializer.Serialize(new
                    {
                        enrollmentId = enrollment.Id,
                        readerId,
                        registeredByStaffId = enrollment.RequestedByStaffId
                    }))));
            }
            else
            {
                var reason = outcome switch
                {
                    FingerprintEnrollmentOutcome.LowQuality => IdentificationFailureReason.LowQualityCapture,
                    FingerprintEnrollmentOutcome.SensorError => IdentificationFailureReason.ProviderUnavailable,
                    _ => IdentificationFailureReason.Other
                };
                resolved = resolved with { Status = FingerprintEnrollmentStatus.Failed, FailureReason = reason };
                _writes.Add(ClinicalAuditEntry.Success(
                    device, AuditActionType.Update, FingerprintEnrollmentService.EnrollmentEntityName, enrollment.Id.ToString(), now,
                    new AuditContext(PatientId: enrollment.PatientId, ChangesJson: JsonSerializer.Serialize(new
                    {
                        status = new[] { enrollment.Status.ToString(), resolved.Status.ToString() },
                        failure = outcome.ToString()
                    }))));
            }

            _enrollments.Update(resolved, FingerprintEnrollmentStatus.Pending);
            try
            {
                await _writes.SaveChangesAsync(cancellationToken);
            }
            catch (ResourceConflictException exception)
            {
                // Another result, a cancellation, an expiry or the same slot taken concurrently committed first: the same
                // result is a replay.
                var current = await _enrollments.FindAsync(enrollment.Id, cancellationToken);
                if (current?.ResultHash == resultHash)
                {
                    return;
                }

                throw new ResourceConflictException(NotAccepted, exception);
            }
        }

        private static void EnsureSameResult(FingerprintEnrollment enrollment, string resultHash)
        {
            if (!string.Equals(enrollment.ResultHash, resultHash, StringComparison.Ordinal))
            {
                throw new ResourceConflictException(NotAccepted);
            }
        }

        // slot: required (non-negative) for Enrolled, absent otherwise. Its range depends on the sensor and is not fixed here.
        private static (FingerprintEnrollmentOutcome Outcome, int? Slot) Result(SubmitFingerprintEnrollmentResultRequest request)
        {
            if (!Enum.IsDefined(request.Outcome))
            {
                throw new RequestValidationException("outcome is not valid.", "outcome");
            }

            if (request.Outcome == FingerprintEnrollmentOutcome.Enrolled)
            {
                return request.Slot is >= 0
                    ? (request.Outcome, request.Slot)
                    : throw new RequestValidationException("slot is required for an enrollment and cannot be negative.", "slot");
            }

            return request.Slot is null
                ? (request.Outcome, null)
                : throw new RequestValidationException("slot is only allowed for an enrollment.", "slot");
        }
    }
}
