using Microsoft.Extensions.Logging;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.ValueObjects;

namespace SCI_PH.Application.Features.Clinical
{
    // Identification of the NN patient of a prehospital emergency with the patient's QR (Mobile BFF, ADR-015). A QR grants
    // nothing by itself: the staff member must be authenticated, hold the permissions, be active clinical staff and have
    // access to the emergency, all checked BEFORE the QR is looked at, so nobody else learns anything from it.
    //
    // Resolve shows a candidate and changes nothing in the emergency: it is candidate discovery, recorded as a Read audit
    // entry and not as a successful identification. Only the staff member's confirmation identifies it, with exactly one
    // successful IdentificationAttempt: it resolves the QR again (no session, no patient id from the client) and goes through
    // EncounterService.IdentifyPatientAsync, the one way of linking a patient: same encounter, NN code kept, Identify audit
    // entry and PatientId concurrency token. A credential revoked between that lookup and the commit (milliseconds) can still
    // identify: an accepted limit, as a QR only identifies, lasts 15 minutes and needs authorized staff and their confirmation.
    //
    // A QR that is invalid for any reason (malformed, other version, unknown, revoked, expired) is the same 404; only the
    // IdentificationAttempt tells them apart, internally and only as NoMatch or InvalidCredential, with no token or hash.
    public sealed class EmergencyQrIdentificationService
    {
        // Server-controlled operation name of the idempotency receipts; a new version gets a new name.
        public const string ConfirmOperation = "prehospital-emergency.qr-confirm.v1";

        public const string CandidateAuditResource = "PatientQrCandidate";

        private const string Unavailable = "The QR code cannot be read right now. Try again later.";

        private readonly ICurrentClinicalActor _actor;
        private readonly PrehospitalEmergencyService _emergencies;
        private readonly EncounterService _encounters;
        private readonly IPatientQrCredentialStore _credentials;
        private readonly IPatientSelfReadStore _profiles;
        private readonly IPatientAccessAuthorizer _patientAccess;
        private readonly IClinicalWriteStore _writes;
        private readonly IIdempotencyStore _receipts;
        private readonly TimeProvider _time;
        private readonly ILogger<EmergencyQrIdentificationService> _logger;

        public EmergencyQrIdentificationService(
            ICurrentClinicalActor actor,
            PrehospitalEmergencyService emergencies,
            EncounterService encounters,
            IPatientQrCredentialStore credentials,
            IPatientSelfReadStore profiles,
            IPatientAccessAuthorizer patientAccess,
            IClinicalWriteStore writes,
            IIdempotencyStore receipts,
            TimeProvider time,
            ILogger<EmergencyQrIdentificationService> logger)
        {
            _actor = actor;
            _emergencies = emergencies;
            _encounters = encounters;
            _credentials = credentials;
            _profiles = profiles;
            _patientAccess = patientAccess;
            _writes = writes;
            _receipts = receipts;
            _time = time;
            _logger = logger;
        }

        // The candidate reveals personal data, so it fails closed: without its audit entry it is not returned (generic 503).
        public async Task<PatientQrCandidate> ResolveAsync(Guid emergencyId, ResolvePatientQrRequest request, CancellationToken cancellationToken)
        {
            var actor = await _actor.GetAsync(cancellationToken);
            var encounter = await LoadIdentifiableAsync(actor, emergencyId, cancellationToken);
            var credential = await FindValidAsync(actor, encounter.Id, request.Payload, cancellationToken);
            await EnsureCanReadAsync(credential.PatientId, cancellationToken);
            var profile = await _profiles.GetProfileAsync(credential.PatientId, cancellationToken)
                ?? throw new InvalidOperationException("The patient of a QR credential was not found.");

            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Read, CandidateAuditResource, credential.Id.ToString(), _time.GetUtcNow(),
                new AuditContext(credential.PatientId, encounter.Id)));
            try
            {
                await _writes.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError("QR candidate not returned: its audit failed with {ExceptionType}.", exception.GetType().Name);
                throw new ExternalServiceUnavailableException(Unavailable);
            }

            return new PatientQrCandidate(
                $"{profile.GivenNames} {profile.FamilyNames}",
                profile.DateOfBirth,
                profile.Document is { } document
                    ? new PatientIdentifierSummary(document.Type, ClinicalMapping.Mask(document.Type, document.NormalizedValue))
                    : null);
        }

        // Same key and same QR again (also when it races the first one, or after the QR expired): the identified emergency,
        // nothing new. Same key with another QR or emergency: 409. Already identified by another request: 409.
        public async Task<PrehospitalEmergencySummary> ConfirmAsync(Guid emergencyId, ConfirmPatientQrRequest request, CancellationToken cancellationToken)
        {
            var clientRequestId = ClinicalInput.Id(request.ClientRequestId, "clientRequestId");
            var actor = await _actor.GetAsync(cancellationToken);
            var key = new IdempotencyKey(actor.IdentityUserId, ConfirmOperation, clientRequestId);
            var requestHash = IdempotencyRequestHash.Compute(emergencyId.ToString("D"), PatientQrToken.ReceiptFingerprint(request.Payload));

            if (await _receipts.FindAsync(key, cancellationToken) is { } existing)
            {
                return await ReplayAsync(actor, existing, requestHash, cancellationToken);
            }

            ClinicalEncounter encounter;
            try
            {
                encounter = await LoadIdentifiableAsync(actor, emergencyId, cancellationToken);
            }
            catch (ResourceConflictException)
            {
                // The same request may have committed between the lookup above and this load (a double submit): only its
                // own receipt (same user, operation and key) replays, and only with the same data. An identification by any
                // other request or method has no such receipt and stays a 409.
                if (await _receipts.FindAsync(key, cancellationToken) is { } winner)
                {
                    return await ReplayAsync(actor, winner, requestHash, cancellationToken);
                }

                throw;
            }

            var credential = await FindValidAsync(actor, encounter.Id, request.Payload, cancellationToken);
            await EnsureCanReadAsync(credential.PatientId, cancellationToken);

            // Tracked only: written by the SaveChangesAsync of IdentifyPatientAsync, in the same transaction.
            var now = _time.GetUtcNow();
            _writes.Add(Succeeded(actor, encounter.Id, credential, now));
            _receipts.Add(new IdempotencyReceipt(key, requestHash, encounter.Id, now));
            try
            {
                await _encounters.IdentifyPatientAsync(encounter.Id, new IdentifyEncounterPatientRequest(credential.PatientId), cancellationToken);
            }
            catch (IdempotencyKeyCollisionException)
            {
                // A concurrent request with the same key committed first; this one wrote nothing.
                var winner = await _receipts.FindAsync(key, cancellationToken)
                    ?? throw new InvalidOperationException("The idempotency receipt that won the race was not found.");
                return await ReplayAsync(actor, winner, requestHash, cancellationToken);
            }
            catch (ResourceConflictException)
            {
                // Already identified, or lost the PatientId race: a replay only if this same request is the one that won.
                if (await _receipts.FindAsync(key, cancellationToken) is { } winner)
                {
                    return await ReplayAsync(actor, winner, requestHash, cancellationToken);
                }

                throw;
            }

            return await _emergencies.ReadAsync(actor, encounter.Id, cancellationToken);
        }

        private async Task<ClinicalEncounter> LoadIdentifiableAsync(ClinicalActor actor, Guid emergencyId, CancellationToken cancellationToken)
        {
            var encounter = await _emergencies.LoadForIdentificationAsync(actor, emergencyId, cancellationToken);
            EmergencyFingerprintService.EnsureIdentifiable(encounter);
            return encounter;
        }

        private async Task EnsureCanReadAsync(Guid patientId, CancellationToken cancellationToken)
        {
            if (!await _patientAccess.CanReadAsync(patientId, Permissions.Patients.Read, cancellationToken))
            {
                throw new AccessDeniedException("The current user cannot read this patient.");
            }
        }

        private async Task<PatientIdentifier> FindValidAsync(ClinicalActor actor, Guid encounterId, string? payload, CancellationToken cancellationToken)
        {
            var now = _time.GetUtcNow();
            var reason = IdentificationFailureReason.NoMatch;
            if (PatientQrToken.TryHash(payload, out var hash) && await _credentials.FindByHashAsync(hash, cancellationToken) is { } credential)
            {
                if (credential.IsValidAt(now))
                {
                    return credential;
                }

                reason = IdentificationFailureReason.InvalidCredential;
            }

            await RecordFailureAsync(actor, encounterId, reason, now, cancellationToken);
            throw new ResourceNotFoundException("PatientQr", "The QR code is not valid.");
        }

        // Best effort: the answer is the same 404 whether or not this is recorded.
        private async Task RecordFailureAsync(
            ClinicalActor actor, Guid encounterId, IdentificationFailureReason reason, DateTimeOffset now, CancellationToken cancellationToken)
        {
            try
            {
                _writes.Add(IdentificationAttempt.Failed(IdentificationMethod.QrCode, reason, now, Context(actor, encounterId)));
                await _writes.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning("Failed QR identification not recorded: {ExceptionType}.", exception.GetType().Name);
            }
        }

        private async Task<PrehospitalEmergencySummary> ReplayAsync(
            ClinicalActor actor, IdempotencyReceipt receipt, string requestHash, CancellationToken cancellationToken)
        {
            PrehospitalEmergencyService.EnsureSameRequest(receipt, requestHash);
            return await _emergencies.ReadAsync(actor, receipt.ResourceId, cancellationToken);
        }

        private static IdentificationAttempt Succeeded(ClinicalActor actor, Guid encounterId, PatientIdentifier credential, DateTimeOffset now) =>
            IdentificationAttempt.Succeeded(
                IdentificationMethod.QrCode, credential.PatientId, now, Context(actor, encounterId), patientIdentifierId: credential.Id);

        private static IdentificationAttemptContext Context(ClinicalActor actor, Guid encounterId) =>
            new(ActorIdentityUserId: actor.IdentityUserId, PerformedByStaffId: actor.StaffId, ClinicalEncounterId: encounterId);
    }
}
