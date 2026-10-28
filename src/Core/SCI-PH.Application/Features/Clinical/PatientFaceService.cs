using System.Text.Json;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Features.Clinical
{
    // Assisted 1:1 face verification of a KNOWN patient from the hospital Web (ADR-012): staff enroll the patient's face,
    // verify a live capture against THAT patient's active face reference only (never a search among patients) and revoke
    // it. No liveness: a tool for staff who are present, not an authentication. A Match never identifies an encounter or
    // changes anything clinical; it is only recorded in the audit. Captures are memory only; the clinical schema keeps
    // only the provider's opaque SubjectReference; scores and thresholds stay inside the provider.
    public sealed class PatientFaceService
    {
        public const string EnrollOperation = "patient-face.enroll.v1";
        public const string VerifyOperation = "patient-face.verify.v1";
        public const string VerificationEntityName = "FaceVerification";

        private const string NoActiveFace = "The patient has no active face reference to verify against.";
        private const string ProviderUnavailable = "The face verification service is not available. Try again later.";

        private readonly ICurrentClinicalActor _actor;
        private readonly IPatientAccessAuthorizer _patientAccess;
        private readonly IClinicalReadStore _reads;
        private readonly IClinicalWriteStore _writes;
        private readonly IIdempotencyStore _receipts;
        private readonly IFaceVerificationService _faces;
        private readonly IFaceCaptureNormalizer _captures;
        private readonly IFaceTemplateDeletionStore _deletions;
        private readonly FaceTemplateCleanupService _cleanup;
        private readonly TimeProvider _time;

        public PatientFaceService(
            ICurrentClinicalActor actor,
            IPatientAccessAuthorizer patientAccess,
            IClinicalReadStore reads,
            IClinicalWriteStore writes,
            IIdempotencyStore receipts,
            IFaceVerificationService faces,
            IFaceCaptureNormalizer captures,
            IFaceTemplateDeletionStore deletions,
            FaceTemplateCleanupService cleanup,
            TimeProvider time)
        {
            _actor = actor;
            _patientAccess = patientAccess;
            _reads = reads;
            _writes = writes;
            _receipts = receipts;
            _faces = faces;
            _captures = captures;
            _deletions = deletions;
            _cleanup = cleanup;
            _time = time;
        }

        // The patient's face references (active and revoked), newest first: for staff who manage biometrics or verify.
        public async Task<IReadOnlyList<PatientFaceSummary>> ListAsync(Guid patientId, CancellationToken cancellationToken)
        {
            await _actor.GetAsync(cancellationToken);
            await EnsurePatientAsync(patientId, cancellationToken, Permissions.Biometrics.Manage, Permissions.Patients.Identify);
            return (await _reads.ListFaceReferencesAsync(patientId, cancellationToken)).Select(Summary).ToList();
        }

        // Biometrics.Manage. The capture is normalized, then (unless this request already completed: same key and same
        // capture, 200 without calling the provider again) the provider enrolls it; Enrolled creates the Active face
        // reference, its audit entry and the receipt in one transaction (201). NoFace, MultipleFaces and LowQuality create
        // nothing (200). The patient already has an active face: 409 before the provider is called; a concurrent enrollment
        // that commits first: 409 on the unique index. Whenever the clinical commit fails after the provider enrolled, the
        // provider's subject is deleted (best effort) and the original error is kept.
        public async Task<EnrollFaceResult> EnrollAsync(Guid patientId, FaceCaptureUpload upload, CancellationToken cancellationToken)
        {
            var clientRequestId = ClinicalInput.Id(upload.ClientRequestId, "clientRequestId");
            var actor = await _actor.GetAsync(cancellationToken);
            await EnsurePatientAsync(patientId, cancellationToken, Permissions.Biometrics.Manage);
            var capture = await _captures.NormalizeAsync(upload.Content, upload.Length, cancellationToken);
            var key = new IdempotencyKey(actor.IdentityUserId, EnrollOperation, clientRequestId);
            var requestHash = IdempotencyRequestHash.Compute(patientId.ToString("D"), capture.ContentHash);
            if (await _receipts.FindAsync(key, cancellationToken) is { } existing)
            {
                return new EnrollFaceResult(await ReplayEnrollAsync(existing, requestHash, patientId, cancellationToken), Created: false);
            }

            if (await _reads.FindActiveFaceReferenceAsync(patientId, cancellationToken) is not null)
            {
                throw new ResourceConflictException("The patient already has an active face reference.");
            }

            var enrolled = await _faces.EnrollAsync(capture, cancellationToken);
            if (enrolled.Outcome != FaceEnrollmentOutcome.Enrolled)
            {
                return new EnrollFaceResult(new FaceEnrollmentResponse(enrolled.Outcome, Face: null), Created: false);
            }

            var subjectReference = enrolled.SubjectReference
                ?? throw new InvalidOperationException("The face provider enrolled without a subject reference.");
            try
            {
                var now = _time.GetUtcNow();
                var reference = PatientBiometricReference.Register(
                    patientId, BiometricModality.Face, _faces.Provider, subjectReference, actor.StaffId, _faces.AlgorithmVersion);
                _writes.Add(reference);
                _writes.Add(ClinicalAuditEntry.Success(
                    actor.AuditActor, AuditActionType.Create, nameof(PatientBiometricReference), reference.Id.ToString(), now,
                    new AuditContext(PatientId: patientId, ChangesJson: JsonSerializer.Serialize(new
                    {
                        modality = BiometricModality.Face.ToString(),
                        provider = _faces.Provider,
                        algorithmVersion = _faces.AlgorithmVersion
                    }))));
                _receipts.Add(new IdempotencyReceipt(key, requestHash, reference.Id, now));
                await _writes.SaveChangesAsync(cancellationToken);
                return new EnrollFaceResult(new FaceEnrollmentResponse(FaceEnrollmentOutcome.Enrolled, await SummaryAsync(patientId, reference.Id, cancellationToken)), Created: true);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Nothing clinical was written: the provider's template must not outlive it.
                await CompensateAsync(subjectReference);
                if (exception is IdempotencyKeyCollisionException or ResourceConflictException
                    && await _receipts.FindAsync(key, cancellationToken) is { } winner)
                {
                    // The same request committed concurrently: answer as its replay.
                    return new EnrollFaceResult(await ReplayEnrollAsync(winner, requestHash, patientId, cancellationToken), Created: false);
                }

                throw;
            }
        }

        // Patients.Identify. The capture is compared ONLY with the patient's active face reference, chosen by the server
        // (no reference id from the client): Match, NoMatch, NoFace, MultipleFaces or LowQuality, all 200 and recorded as a
        // FaceVerification audit entry (outcome, provider, algorithm version; never a score, threshold or subject
        // reference) with the receipt, in one transaction. Nothing else changes: no encounter is identified. Same key and
        // same capture again: the recorded outcome, without calling the provider. No active face reference, or one of
        // another provider: 409. A revocation committed before the record makes it lose (409).
        public async Task<FaceVerificationResponse> VerifyAsync(Guid patientId, FaceCaptureUpload upload, CancellationToken cancellationToken)
        {
            var clientRequestId = ClinicalInput.Id(upload.ClientRequestId, "clientRequestId");
            var actor = await _actor.GetAsync(cancellationToken);
            await EnsurePatientAsync(patientId, cancellationToken, Permissions.Patients.Identify);
            var capture = await _captures.NormalizeAsync(upload.Content, upload.Length, cancellationToken);
            var key = new IdempotencyKey(actor.IdentityUserId, VerifyOperation, clientRequestId);
            var requestHash = IdempotencyRequestHash.Compute(patientId.ToString("D"), capture.ContentHash);
            if (await _receipts.FindAsync(key, cancellationToken) is { } existing)
            {
                return await ReplayVerifyAsync(existing, requestHash, patientId, cancellationToken);
            }

            var row = await _reads.FindActiveFaceReferenceAsync(patientId, cancellationToken);
            var reference = row is null ? null : await _writes.FindBiometricReferenceAsync(row.Id, cancellationToken);
            if (reference is not { Status: BiometricReferenceStatus.Active, Modality: BiometricModality.Face } || reference.PatientId != patientId)
            {
                throw new ResourceConflictException(NoActiveFace);
            }

            // Ownership: only a reference of THIS provider and algorithm version is ever handed to it. Another one (the Fake,
            // a former provider, an unsupported version) needs a re-enrollment; it is never interpreted (503).
            if (!string.Equals(reference.Provider, _faces.Provider, StringComparison.Ordinal)
                || !string.Equals(reference.AlgorithmVersion, _faces.AlgorithmVersion, StringComparison.Ordinal))
            {
                throw new ExternalServiceUnavailableException(ProviderUnavailable);
            }

            var outcome = await _faces.VerifyAsync(reference.SubjectReference, capture, cancellationToken);
            var now = _time.GetUtcNow();
            // The reference must still be active when the result is recorded (Status concurrency token).
            _writes.EnsureBiometricReferenceUnchanged(reference);
            var verificationId = Guid.NewGuid();
            var audit = ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Read, VerificationEntityName, verificationId.ToString(), now,
                new AuditContext(PatientId: patientId, ChangesJson: JsonSerializer.Serialize(new
                {
                    outcome = outcome.ToString(),
                    biometricReferenceId = reference.Id,
                    provider = reference.Provider,
                    algorithmVersion = reference.AlgorithmVersion
                })));
            _writes.Add(audit);
            _receipts.Add(new IdempotencyReceipt(key, requestHash, audit.Id, now));
            try
            {
                await _writes.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is IdempotencyKeyCollisionException or ResourceConflictException)
            {
                if (await _receipts.FindAsync(key, cancellationToken) is { } winner)
                {
                    return await ReplayVerifyAsync(winner, requestHash, patientId, cancellationToken);
                }

                throw new ResourceConflictException(NoActiveFace, exception);
            }

            return new FaceVerificationResponse(audit.Id, outcome);
        }

        // Biometrics.Manage. Active -> Revoked with its audit entry (reason only there) and ONE Pending template deletion job,
        // in one transaction: from that commit it is never verified again. Then one immediate cleanup attempt; if it fails the
        // job stays Pending and FaceTemplateCleanupService retries it (security never depends on it). Already Revoked: 200
        // with its state, nothing written. Another patient's reference or one that is not a face: 404.
        public async Task<PatientFaceSummary> RevokeAsync(
            Guid patientId, Guid referenceId, RevokeFaceRequest? request, CancellationToken cancellationToken)
        {
            var reason = request?.Reason;
            if (reason is { } given && !Enum.IsDefined(given))
            {
                throw new RequestValidationException("reason is not valid.", "reason");
            }

            var actor = await _actor.GetAsync(cancellationToken);
            await EnsurePatientAsync(patientId, cancellationToken, Permissions.Biometrics.Manage);
            for (var attempt = 1; ; attempt++)
            {
                var reference = await _writes.FindBiometricReferenceAsync(referenceId, cancellationToken);
                if (reference is null || reference.PatientId != patientId || reference.Modality != BiometricModality.Face)
                {
                    throw new ResourceNotFoundException(nameof(PatientBiometricReference), referenceId);
                }

                if (reference.Status == BiometricReferenceStatus.Revoked)
                {
                    return await SummaryAsync(patientId, referenceId, cancellationToken);
                }

                var now = _time.GetUtcNow();
                reference.Revoke(now);
                _writes.Add(ClinicalAuditEntry.Success(
                    actor.AuditActor, AuditActionType.Update, nameof(PatientBiometricReference), reference.Id.ToString(), now,
                    new AuditContext(PatientId: patientId, ChangesJson: JsonSerializer.Serialize(new
                    {
                        status = new[] { BiometricReferenceStatus.Active.ToString(), reference.Status.ToString() },
                        reason = reason?.ToString()
                    }))));
                var deletion = FaceTemplateDeletion.Request(reference.Id, now);
                _deletions.Add(deletion);
                try
                {
                    await _writes.SaveChangesAsync(cancellationToken);
                }
                catch (ResourceConflictException) when (attempt < 2)
                {
                    // Another revocation committed first; nothing of this one was written. Read it again: now Revoked.
                    continue;
                }

                await TryCleanUpNowAsync(deletion.Id, cancellationToken);
                return await SummaryAsync(patientId, referenceId, cancellationToken);
            }
        }

        // The immediate attempt never fails the revocation (already committed): the job stays Pending for the worker.
        private async Task TryCleanUpNowAsync(Guid deletionId, CancellationToken cancellationToken)
        {
            try
            {
                await _cleanup.TryCompleteAsync(deletionId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Durability comes from the job.
            }
        }

        private async Task CompensateAsync(string subjectReference)
        {
            try
            {
                await _faces.DeleteAsync(subjectReference, CancellationToken.None);
            }
            catch (Exception)
            {
                // Best effort: an orphan template of a real provider is left to its reconciliation (ADR-012).
            }
        }

        // The patient exists (404) and the actor may see it (Patients.Read) and holds one of the permissions (403).
        private async Task EnsurePatientAsync(Guid patientId, CancellationToken cancellationToken, params string[] anyOf)
        {
            if (!await _reads.PatientExistsAsync(patientId, cancellationToken))
            {
                throw new ResourceNotFoundException(nameof(Patient), patientId);
            }

            if (!await _patientAccess.CanReadAsync(patientId, Permissions.Patients.Read, cancellationToken))
            {
                throw new AccessDeniedException("The current user cannot work with this patient's face references.");
            }

            foreach (var permission in anyOf)
            {
                if (await _patientAccess.CanWriteAsync(patientId, permission, cancellationToken))
                {
                    return;
                }
            }

            throw new AccessDeniedException("The current user cannot work with this patient's face references.");
        }

        private async Task<FaceEnrollmentResponse> ReplayEnrollAsync(
            IdempotencyReceipt receipt, string requestHash, Guid patientId, CancellationToken cancellationToken)
        {
            PrehospitalEmergencyService.EnsureSameRequest(receipt, requestHash);
            return new FaceEnrollmentResponse(FaceEnrollmentOutcome.Enrolled, await SummaryAsync(patientId, receipt.ResourceId, cancellationToken));
        }

        // The receipt's resource is the verification's audit entry: its recorded outcome is the answer.
        private async Task<FaceVerificationResponse> ReplayVerifyAsync(
            IdempotencyReceipt receipt, string requestHash, Guid patientId, CancellationToken cancellationToken)
        {
            PrehospitalEmergencyService.EnsureSameRequest(receipt, requestHash);
            var changes = await _reads.GetAuditChangesAsync(receipt.ResourceId, VerificationEntityName, patientId, cancellationToken)
                ?? throw new InvalidOperationException("The face verification of a receipt was not found.");
            using var document = JsonDocument.Parse(changes);
            var outcome = Enum.Parse<FaceVerificationOutcome>(document.RootElement.GetProperty("outcome").GetString()!);
            return new FaceVerificationResponse(receipt.ResourceId, outcome);
        }

        private async Task<PatientFaceSummary> SummaryAsync(Guid patientId, Guid referenceId, CancellationToken cancellationToken) =>
            Summary((await _reads.ListFaceReferencesAsync(patientId, cancellationToken)).Single(r => r.Id == referenceId));

        private static PatientFaceSummary Summary(FaceReferenceRow row) =>
            new(row.Id, row.Status, row.Provider, row.CreatedAtUtc, row.RevokedAtUtc);
    }
}
