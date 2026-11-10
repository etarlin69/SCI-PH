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

namespace SCI_PH.Application.Features.Clinical
{
    // Identification of the NN patient of a prehospital emergency by document (Mobile BFF), with the same safety principle
    // as the QR and the fingerprint: resolve a candidate, show it, and identify only after the staff member confirms it.
    // The staff member must be authenticated, hold the permissions, be active clinical staff and have access to the
    // emergency (assignment, before the handoff, not identified yet), all checked BEFORE the document is looked up, so an
    // unassigned user learns nothing about who holds a document.
    //
    // Resolve shows a candidate and changes nothing in the emergency: it is candidate discovery, recorded as a Read audit
    // entry and not as a successful identification. Confirm resolves the document again (no patient id from the client,
    // nothing of the candidate is trusted) and goes through EncounterService.IdentifyPatientAsync, the one way of linking a
    // patient: same encounter, NN code kept, Identify audit entry and PatientId concurrency token, with exactly one
    // successful IdentificationAttempt (NationalId or Passport, with the matched identifier) in the same transaction.
    //
    // A document no patient holds in the platform scope is the same 404 at both steps, and a failed IdentificationAttempt
    // (NoMatch, no document): that is what makes guessing documents visible. Requests the emergency rejects (no access,
    // handed off, already identified) or that are malformed are not identification attempts and write none. The document
    // never goes to an audit entry, an attempt or a log; the receipt holds only its hash.
    //
    // Accepted limit, as with the QR: the server cannot know which candidate the staff member saw, so a document moved to
    // another patient between resolve and confirm identifies the patient holding it at confirmation. Documents are unique
    // per type in the platform scope and are not reassigned in normal operation.
    public sealed class EmergencyDocumentIdentificationService
    {
        // Server-controlled operation name of the idempotency receipts. Unchanged from the one-step identification by
        // document, so a retry of a request sent before the confirmation step existed is still recognized.
        public const string ConfirmOperation = "prehospital-emergency.identify-document.v1";

        public const string CandidateAuditResource = "PatientDocumentCandidate";

        private const string Unavailable = "The document cannot be looked up right now. Try again later.";
        private const string NotFound = "No patient was found with this document.";

        private readonly ICurrentClinicalActor _actor;
        private readonly PrehospitalEmergencyService _emergencies;
        private readonly EncounterService _encounters;
        private readonly IClinicalReadStore _reads;
        private readonly IPatientAccessAuthorizer _patientAccess;
        private readonly IClinicalWriteStore _writes;
        private readonly IIdempotencyStore _receipts;
        private readonly TimeProvider _time;
        private readonly ILogger<EmergencyDocumentIdentificationService> _logger;

        public EmergencyDocumentIdentificationService(
            ICurrentClinicalActor actor,
            PrehospitalEmergencyService emergencies,
            EncounterService encounters,
            IClinicalReadStore reads,
            IPatientAccessAuthorizer patientAccess,
            IClinicalWriteStore writes,
            IIdempotencyStore receipts,
            TimeProvider time,
            ILogger<EmergencyDocumentIdentificationService> logger)
        {
            _actor = actor;
            _emergencies = emergencies;
            _encounters = encounters;
            _reads = reads;
            _patientAccess = patientAccess;
            _writes = writes;
            _receipts = receipts;
            _time = time;
            _logger = logger;
        }

        // The candidate reveals personal data, so it fails closed: without its audit entry it is not returned (generic 503).
        public async Task<PatientDocumentCandidate> ResolveAsync(
            Guid emergencyId, ResolveEmergencyDocumentRequest request, CancellationToken cancellationToken)
        {
            var documentType = PrehospitalEmergencyService.DocumentType(request.DocumentType);
            var documentNumber = PrehospitalEmergencyService.DocumentNumber(documentType, request.DocumentNumber);
            var actor = await _actor.GetAsync(cancellationToken);
            var encounter = await LoadIdentifiableAsync(actor, emergencyId, cancellationToken);
            var match = await FindAsync(actor, encounter.Id, documentType, documentNumber, cancellationToken);
            var patient = await _reads.GetPatientAsync(match.PatientId, cancellationToken)
                ?? throw new InvalidOperationException("The patient of an active identifier was not found.");

            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Read, CandidateAuditResource, match.IdentifierId.ToString(), _time.GetUtcNow(),
                new AuditContext(match.PatientId, encounter.Id)));
            try
            {
                await _writes.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError("Document candidate not returned: its audit failed with {ExceptionType}.", exception.GetType().Name);
                throw new ExternalServiceUnavailableException(Unavailable);
            }

            return new PatientDocumentCandidate(
                $"{patient.GivenNames} {patient.FamilyNames}",
                patient.DateOfBirth,
                new PatientIdentifierSummary(documentType, ClinicalMapping.Mask(documentType, documentNumber)));
        }

        // Same key and same document again (also when it races the first one): the identified emergency, nothing new.
        // Same key with another document, type or emergency: 409. Already identified, by this or any other method: 409.
        // Once a clinic received the emergency: 409 for the paramedics who took part (LoadForIdentificationAsync).
        public async Task<PrehospitalEmergencySummary> ConfirmAsync(
            Guid emergencyId, IdentifyEmergencyByDocumentRequest request, CancellationToken cancellationToken)
        {
            var clientRequestId = ClinicalInput.Id(request.ClientRequestId, "clientRequestId");
            var documentType = PrehospitalEmergencyService.DocumentType(request.DocumentType);
            var documentNumber = PrehospitalEmergencyService.DocumentNumber(documentType, request.DocumentNumber);
            var actor = await _actor.GetAsync(cancellationToken);
            var key = new IdempotencyKey(actor.IdentityUserId, ConfirmOperation, clientRequestId);
            // The normalized value, never the raw one: equivalent spellings of the same document are the same request.
            var requestHash = IdempotencyRequestHash.Compute(emergencyId.ToString("D"), documentType.ToString(), documentNumber);

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

            var match = await FindAsync(actor, encounter.Id, documentType, documentNumber, cancellationToken);

            // Tracked only: written by the SaveChangesAsync of IdentifyPatientAsync, in the same transaction.
            var now = _time.GetUtcNow();
            _writes.Add(IdentificationAttempt.Succeeded(
                Method(documentType), match.PatientId, now, Context(actor, encounter.Id), patientIdentifierId: match.IdentifierId));
            _receipts.Add(new IdempotencyReceipt(key, requestHash, encounter.Id, now));
            try
            {
                await _encounters.IdentifyPatientAsync(encounter.Id, new IdentifyEncounterPatientRequest(match.PatientId), cancellationToken);
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

        public static IdentificationMethod Method(PatientIdentifierType documentType) => documentType switch
        {
            PatientIdentifierType.NationalId => IdentificationMethod.NationalId,
            PatientIdentifierType.Passport => IdentificationMethod.Passport,
            _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, "Not a document type of the emergency.")
        };

        private async Task<ClinicalEncounter> LoadIdentifiableAsync(ClinicalActor actor, Guid emergencyId, CancellationToken cancellationToken)
        {
            var encounter = await _emergencies.LoadForIdentificationAsync(actor, emergencyId, cancellationToken);
            EmergencyFingerprintService.EnsureIdentifiable(encounter);
            return encounter;
        }

        // The patient holding the document, readable by the staff member. Anything else is the same 404, so the answer never
        // tells a patient that cannot be read from a document nobody holds.
        private async Task<PatientIdentifierMatch> FindAsync(
            ClinicalActor actor, Guid encounterId, PatientIdentifierType documentType, string documentNumber, CancellationToken cancellationToken)
        {
            if (await _reads.FindPlatformIdentifierAsync(documentType, documentNumber, cancellationToken) is { } match
                && await _patientAccess.CanReadAsync(match.PatientId, Permissions.Patients.Read, cancellationToken))
            {
                return match;
            }

            await RecordFailureAsync(actor, encounterId, Method(documentType), cancellationToken);
            throw new ResourceNotFoundException(nameof(Patient), NotFound);
        }

        // Best effort: the answer is the same 404 whether or not this is recorded.
        private async Task RecordFailureAsync(
            ClinicalActor actor, Guid encounterId, IdentificationMethod method, CancellationToken cancellationToken)
        {
            try
            {
                _writes.Add(IdentificationAttempt.Failed(
                    method, IdentificationFailureReason.NoMatch, _time.GetUtcNow(), Context(actor, encounterId)));
                await _writes.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning("Failed document identification not recorded: {ExceptionType}.", exception.GetType().Name);
            }
        }

        private async Task<PrehospitalEmergencySummary> ReplayAsync(
            ClinicalActor actor, IdempotencyReceipt receipt, string requestHash, CancellationToken cancellationToken)
        {
            PrehospitalEmergencyService.EnsureSameRequest(receipt, requestHash);
            return await _emergencies.ReadAsync(actor, receipt.ResourceId, cancellationToken);
        }

        private static IdentificationAttemptContext Context(ClinicalActor actor, Guid encounterId) =>
            new(ActorIdentityUserId: actor.IdentityUserId, PerformedByStaffId: actor.StaffId, ClinicalEncounterId: encounterId);
    }
}
