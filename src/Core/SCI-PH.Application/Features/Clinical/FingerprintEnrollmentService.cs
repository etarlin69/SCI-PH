using System.Text.Json;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Biometrics;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Features.Clinical
{
    // Fingerprint enrollment of a registered patient from the hospital Web (ADR-010). Staff with Biometrics.Manage open a
    // session on a reader that can both enroll and identify; the reader stores the template on its sensor, in a slot it
    // chooses, and reports it (FingerprintEnrollmentReaderService), which creates the PatientBiometricReference. The
    // permission alone is not enough: the actor must be active clinical staff (ICurrentClinicalActor, no role bypass) who
    // can see the patient (the same rule as reading the patient). At most one active fingerprint reference per patient and
    // reader, one Pending enrollment per patient and per reader.
    public sealed class FingerprintEnrollmentService
    {
        // Server-controlled operation name of the idempotency receipts; a new version gets a new name.
        public const string OpenOperation = "fingerprint-enrollment.open.v1";

        public const string EnrollmentEntityName = "FingerprintEnrollmentSession";

        private readonly ICurrentClinicalActor _actor;
        private readonly IPatientAccessAuthorizer _patientAccess;
        private readonly IClinicalReadStore _reads;
        private readonly IClinicalWriteStore _writes;
        private readonly IIdempotencyStore _receipts;
        private readonly IFingerprintEnrollmentStore _enrollments;
        private readonly IDeviceCredentialStore _readers;
        private readonly IFingerprintSlotDeletionStore _deletions;
        private readonly TimeProvider _time;

        public FingerprintEnrollmentService(
            ICurrentClinicalActor actor,
            IPatientAccessAuthorizer patientAccess,
            IClinicalReadStore reads,
            IClinicalWriteStore writes,
            IIdempotencyStore receipts,
            IFingerprintEnrollmentStore enrollments,
            IDeviceCredentialStore readers,
            IFingerprintSlotDeletionStore deletions,
            TimeProvider time)
        {
            _actor = actor;
            _patientAccess = patientAccess;
            _reads = reads;
            _writes = writes;
            _receipts = receipts;
            _enrollments = enrollments;
            _readers = readers;
            _deletions = deletions;
            _time = time;
        }

        // A reader the prototype can enroll on: active, and able to enroll AND to identify, so that what it enrolls can be
        // used in an emergency (a reference belongs to its reader).
        public static bool CanEnroll(DeviceSummary device) =>
            device.Status == DeviceCredentialStatus.Active
            && device.Scopes.Contains(DeviceScopes.FingerprintEnroll)
            && device.Scopes.Contains(DeviceScopes.FingerprintIdentify);

        public async Task<IReadOnlyList<FingerprintReaderSummary>> ListEnrollersAsync(CancellationToken cancellationToken)
        {
            await _actor.GetAsync(cancellationToken);
            return (await _readers.ListAsync(cancellationToken))
                .Where(CanEnroll)
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(d => d.DeviceCode, StringComparer.Ordinal)
                .Select(d => new FingerprintReaderSummary(d.Id, d.DeviceCode, d.Name, d.LastSeenAtUtc))
                .ToList();
        }

        // The patient's fingerprint references (active and revoked), newest first, with the reader's code and name only and,
        // for a revoked one, the state of its physical cleanup (ADR-011).
        public async Task<IReadOnlyList<PatientFingerprintSummary>> ListPatientFingerprintsAsync(Guid patientId, CancellationToken cancellationToken)
        {
            await EnsurePatientAsync(await _actor.GetAsync(cancellationToken), patientId, cancellationToken);
            return await PatientFingerprints.SummarizeAsync(
                await _reads.ListFingerprintReferencesAsync(patientId, cancellationToken), _readers, _deletions, cancellationToken);
        }

        // A Pending enrollment of the patient on that reader (server time, expires after EnrollmentTtl), its Create audit
        // entry and the receipt, in one transaction. Unknown patient or reader, revoked reader or one that cannot both
        // enroll and identify: 404. The patient already has an active fingerprint on that reader, or an enrollment is
        // already pending for the patient or the reader: 409. Same key and payload again: the same enrollment (200); same
        // key with another patient or reader: 409.
        public async Task<OpenFingerprintEnrollmentResult> OpenAsync(
            Guid patientId, OpenFingerprintEnrollmentRequest request, CancellationToken cancellationToken)
        {
            var clientRequestId = ClinicalInput.Id(request.ClientRequestId, "clientRequestId");
            var readerId = ClinicalInput.Id(request.DeviceId, "deviceId");
            var actor = await _actor.GetAsync(cancellationToken);
            var key = new IdempotencyKey(actor.IdentityUserId, OpenOperation, clientRequestId);
            var requestHash = IdempotencyRequestHash.Compute(patientId.ToString("D"), readerId.ToString("D"));

            await EnsurePatientAsync(actor, patientId, cancellationToken);
            if (await _receipts.FindAsync(key, cancellationToken) is { } existing)
            {
                return new OpenFingerprintEnrollmentResult(await ReplayOpenAsync(existing, requestHash, cancellationToken), Created: false);
            }

            var reader = await _readers.FindAsync(readerId, cancellationToken);
            if (reader is null || !CanEnroll(reader))
            {
                throw new ResourceNotFoundException("FingerprintReader", "No active enrollment reader was found with this id.");
            }

            if (await _reads.HasActiveFingerprintReferenceAsync(
                patientId, FingerprintReferences.Provider, FingerprintReferences.ReaderPrefix(reader.Id), cancellationToken))
            {
                throw new ResourceConflictException("The patient already has an active fingerprint on this reader.");
            }

            var now = _time.GetUtcNow();
            await _enrollments.ExpireDueAsync(new FingerprintEnrollmentScope(PatientId: patientId, DeviceCredentialId: reader.Id), now, cancellationToken);
            var enrollment = new FingerprintEnrollment(
                Guid.NewGuid(), patientId, reader.Id, actor.StaffId, FingerprintEnrollmentStatus.Pending,
                FailureReason: null, ResultHash: null, BiometricReferenceId: null,
                CreatedAtUtc: now, ExpiresAtUtc: now + FingerprintReferences.EnrollmentTtl, ResolvedAtUtc: null);
            _enrollments.Add(enrollment);
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Create, EnrollmentEntityName, enrollment.Id.ToString(), now,
                new AuditContext(PatientId: patientId, ChangesJson: JsonSerializer.Serialize(new
                {
                    readerId = reader.Id,
                    expiresAtUtc = enrollment.ExpiresAtUtc
                }))));
            _receipts.Add(new IdempotencyReceipt(key, requestHash, enrollment.Id, now));

            if (await SaveAsync(key, cancellationToken) is { } winner)
            {
                return new OpenFingerprintEnrollmentResult(await ReplayOpenAsync(winner, requestHash, cancellationToken), Created: false);
            }

            return new OpenFingerprintEnrollmentResult(Summary(enrollment), Created: true);
        }

        // The state of an enrollment of the patient (404 for another patient's). A Pending one past its expiry is closed as
        // Expired here.
        public async Task<FingerprintEnrollmentSummary> GetAsync(Guid patientId, Guid enrollmentId, CancellationToken cancellationToken)
        {
            await EnsurePatientAsync(await _actor.GetAsync(cancellationToken), patientId, cancellationToken);
            return Summary(await CurrentAsync(await OwnAsync(patientId, enrollmentId, cancellationToken), cancellationToken));
        }

        // Pending -> Cancelled, with an Update audit entry (only on that transition). Already ended without a reference
        // (Cancelled, Expired, Failed): 200 with that state and nothing written. Enrolled: 409 (the reference is not touched;
        // revocation is its own operation). When the reader's result commits first, the decision is taken again on it.
        public async Task<FingerprintEnrollmentSummary> CancelAsync(Guid patientId, Guid enrollmentId, CancellationToken cancellationToken)
        {
            var actor = await _actor.GetAsync(cancellationToken);
            await EnsurePatientAsync(actor, patientId, cancellationToken);
            var enrollment = await OwnAsync(patientId, enrollmentId, cancellationToken);
            for (var attempt = 1; ; attempt++)
            {
                enrollment = await CurrentAsync(enrollment, cancellationToken);
                if (enrollment.Status == FingerprintEnrollmentStatus.Enrolled)
                {
                    throw new ResourceConflictException("A completed fingerprint enrollment cannot be cancelled.");
                }

                if (!enrollment.IsPending)
                {
                    return Summary(enrollment);
                }

                var now = _time.GetUtcNow();
                var cancelled = enrollment with { Status = FingerprintEnrollmentStatus.Cancelled };
                _enrollments.Update(cancelled, FingerprintEnrollmentStatus.Pending);
                _writes.Add(ClinicalAuditEntry.Success(
                    actor.AuditActor, AuditActionType.Update, EnrollmentEntityName, enrollment.Id.ToString(), now,
                    new AuditContext(PatientId: patientId, ChangesJson: JsonSerializer.Serialize(new
                    {
                        status = new[] { enrollment.Status.ToString(), cancelled.Status.ToString() }
                    }))));
                try
                {
                    await _writes.SaveChangesAsync(cancellationToken);
                    return Summary(cancelled);
                }
                catch (ResourceConflictException) when (attempt < 2)
                {
                    // The reader's result or an expiry committed first; nothing of this one was written.
                    enrollment = await _enrollments.FindAsync(enrollment.Id, cancellationToken)
                        ?? throw new InvalidOperationException("A fingerprint enrollment disappeared.");
                }
            }
        }

        // The actor is resolved first (active clinical staff), then the patient rule of PatientFingerprints.
        private Task EnsurePatientAsync(ClinicalActor actor, Guid patientId, CancellationToken cancellationToken) =>
            PatientFingerprints.EnsureCanManageAsync(_reads, _patientAccess, patientId, cancellationToken);

        private async Task<FingerprintEnrollment> OwnAsync(Guid patientId, Guid enrollmentId, CancellationToken cancellationToken)
        {
            var enrollment = await _enrollments.FindAsync(enrollmentId, cancellationToken);
            return enrollment is not null && enrollment.PatientId == patientId
                ? enrollment
                : throw new ResourceNotFoundException(EnrollmentEntityName, enrollmentId);
        }

        private async Task<FingerprintEnrollment> CurrentAsync(FingerprintEnrollment enrollment, CancellationToken cancellationToken)
        {
            var now = _time.GetUtcNow();
            if (!enrollment.IsDue(now))
            {
                return enrollment;
            }

            await _enrollments.ExpireDueAsync(new FingerprintEnrollmentScope(EnrollmentId: enrollment.Id), now, cancellationToken);
            return await _enrollments.FindAsync(enrollment.Id, cancellationToken)
                ?? throw new InvalidOperationException("An expired fingerprint enrollment was not found.");
        }

        public static FingerprintEnrollmentSummary Summary(FingerprintEnrollment enrollment) => new(
            enrollment.Id,
            enrollment.Status,
            enrollment.DeviceCredentialId,
            enrollment.ExpiresAtUtc,
            enrollment.Status == FingerprintEnrollmentStatus.Failed
                ? enrollment.FailureReason switch
                {
                    IdentificationFailureReason.LowQualityCapture => FingerprintEnrollmentOutcome.LowQuality,
                    IdentificationFailureReason.ProviderUnavailable => FingerprintEnrollmentOutcome.SensorError,
                    _ => FingerprintEnrollmentOutcome.Failed
                }
                : null,
            enrollment.BiometricReferenceId);

        // The receipt's resource is the enrollment; the patient and reader are the ones of the request (the hash covers them).
        private async Task<FingerprintEnrollmentSummary> ReplayOpenAsync(IdempotencyReceipt receipt, string requestHash, CancellationToken cancellationToken)
        {
            PrehospitalEmergencyService.EnsureSameRequest(receipt, requestHash);
            var enrollment = await _enrollments.FindAsync(receipt.ResourceId, cancellationToken)
                ?? throw new InvalidOperationException("The fingerprint enrollment of a receipt was not found.");
            return Summary(await CurrentAsync(enrollment, cancellationToken));
        }

        // Returns the receipt of the request that committed instead when this one lost; any other conflict is rethrown.
        private async Task<IdempotencyReceipt?> SaveAsync(IdempotencyKey key, CancellationToken cancellationToken)
        {
            try
            {
                await _writes.SaveChangesAsync(cancellationToken);
                return null;
            }
            catch (IdempotencyKeyCollisionException)
            {
                return await _receipts.FindAsync(key, cancellationToken)
                    ?? throw new InvalidOperationException("The idempotency receipt that won the race was not found.");
            }
            catch (ResourceConflictException)
            {
                if (await _receipts.FindAsync(key, cancellationToken) is { } winner)
                {
                    return winner;
                }

                throw;
            }
        }
    }
}
