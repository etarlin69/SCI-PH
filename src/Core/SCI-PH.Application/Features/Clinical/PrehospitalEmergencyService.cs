using System.Text.Json;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Application.Features.Clinical
{
    // Prehospital emergency opened by a paramedic from the mobile app: a Prehospital encounter WITHOUT a clinic (the
    // paramedic's base clinic is not where the emergency happens) and with an NN patient. The paramedic who opens it is
    // assigned to it (EncounterStaffAssignment, function Paramedic), which is what gives access to this concrete case
    // (IEncounterAccessService). On arrival, TransferService/ReceiveAtClinic sets the destination clinic on the SAME
    // encounter. The NN patient can then be identified by document on that same encounter (EncounterService). Opening and
    // identifying are idempotent per (user, operation, clientRequestId): see IIdempotencyStore.
    public sealed class PrehospitalEmergencyService
    {
        // Server-controlled operation name of the idempotency receipts; a new version gets a new name.
        public const string OpenOperation = "prehospital-emergency.open.v1";
        public const string IdentifyByDocumentOperation = "prehospital-emergency.identify-document.v1";

        // Document types a paramedic can identify with: identity-bearing and typed by hand.
        public static IReadOnlyCollection<PatientIdentifierType> DocumentTypes { get; } =
            [PatientIdentifierType.NationalId, PatientIdentifierType.Passport];

        // startedAt comes from the device clock: a small skew ahead is tolerated, and care opened late (no coverage,
        // a delayed retry) may be recorded up to a day after it started. Both bounds are inclusive.
        public static readonly TimeSpan MaxStartAhead = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan MaxStartAge = TimeSpan.FromHours(24);

        private readonly ICurrentClinicalActor _actor;
        private readonly IEncounterAccessService _access;
        private readonly IClinicalReadStore _reads;
        private readonly IClinicalWriteStore _writes;
        private readonly IIdempotencyStore _receipts;
        private readonly IPatientAccessAuthorizer _patientAccess;
        private readonly EncounterService _encounters;
        private readonly TimeProvider _time;

        public PrehospitalEmergencyService(
            ICurrentClinicalActor actor,
            IEncounterAccessService access,
            IClinicalReadStore reads,
            IClinicalWriteStore writes,
            IIdempotencyStore receipts,
            IPatientAccessAuthorizer patientAccess,
            EncounterService encounters,
            TimeProvider time)
        {
            _actor = actor;
            _access = access;
            _reads = reads;
            _writes = writes;
            _receipts = receipts;
            _patientAccess = patientAccess;
            _encounters = encounters;
            _time = time;
        }

        // First request: encounter + assignment + audit entry + receipt in one transaction (Created = true).
        // Same key and same payload again (also when it races the first one): the same encounter (Created = false).
        // Same key with another payload: 409, nothing written.
        public async Task<OpenPrehospitalEmergencyResult> OpenAsync(OpenPrehospitalEmergencyRequest request, CancellationToken cancellationToken)
        {
            var clientRequestId = ClinicalInput.Id(request.ClientRequestId, "clientRequestId");
            var chiefComplaint = ChiefComplaint(request.ChiefComplaint);
            // Not active clinical staff, or no active affiliation: 403 before anything is read or written.
            var actor = await _actor.GetAsync(cancellationToken);
            var key = new IdempotencyKey(actor.IdentityUserId, OpenOperation, clientRequestId);
            var requestHash = IdempotencyRequestHash.Compute(
                IdempotencyRequestHash.Instant(request.StartedAt), IdempotencyRequestHash.Text(chiefComplaint));

            // A replay is answered even when startedAt has since left the accepted window.
            if (await _receipts.FindAsync(key, cancellationToken) is { } existing)
            {
                return new OpenPrehospitalEmergencyResult(await ReplayAsync(actor, existing, requestHash, cancellationToken), Created: false);
            }

            var now = _time.GetUtcNow();
            var startedAt = StartedAt(request.StartedAt, now);
            var encounter = ClinicalInput.Apply(() => ClinicalEncounter.CreateUnidentified(
                ClinicalEncounterType.Prehospital, startedAt, clinicId: null, reason: chiefComplaint));
            _writes.Add(encounter);
            _writes.Add(EncounterStaffAssignment.Assign(encounter.Id, actor.StaffId, EncounterStaffFunction.Paramedic, now));
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Create, nameof(ClinicalEncounter), encounter.Id.ToString(), now,
                new AuditContext(ClinicalEncounterId: encounter.Id, ChangesJson: JsonSerializer.Serialize(new
                {
                    type = encounter.Type.ToString(),
                    staffFunction = EncounterStaffFunction.Paramedic.ToString()
                }))));
            _receipts.Add(new IdempotencyReceipt(key, requestHash, encounter.Id, now));

            try
            {
                await _writes.SaveChangesAsync(cancellationToken);
            }
            catch (IdempotencyKeyCollisionException)
            {
                // A concurrent request with the same key won the unique index; this one wrote nothing.
                var winner = await _receipts.FindAsync(key, cancellationToken)
                    ?? throw new InvalidOperationException("The idempotency receipt that won the race was not found.");
                return new OpenPrehospitalEmergencyResult(await ReplayAsync(actor, winner, requestHash, cancellationToken), Created: false);
            }

            return new OpenPrehospitalEmergencyResult(await ReadAsync(actor, encounter.Id, cancellationToken), Created: true);
        }

        // Links the NN patient of the emergency to the patient holding that document in the platform scope, on the SAME
        // encounter (EncounterService.IdentifyPatientAsync: access, Domain rule, audit, PatientId concurrency token; the
        // temporary code is kept). No patient is ever created: an unknown document is a 404 that leaves the emergency NN
        // and writes nothing, not even a receipt, so the same document can be tried again once the patient exists.
        // Same key and same payload again (also when it races the first one): the identified emergency, nothing new.
        // Same key with another payload (document, type or emergency): 409. Already identified by another request: 409.
        public async Task<PrehospitalEmergencySummary> IdentifyByDocumentAsync(
            Guid emergencyId, IdentifyEmergencyByDocumentRequest request, CancellationToken cancellationToken)
        {
            var clientRequestId = ClinicalInput.Id(request.ClientRequestId, "clientRequestId");
            var documentType = DocumentType(request.DocumentType);
            var documentNumber = DocumentNumber(documentType, request.DocumentNumber);
            var actor = await _actor.GetAsync(cancellationToken);
            var key = new IdempotencyKey(actor.IdentityUserId, IdentifyByDocumentOperation, clientRequestId);
            // The normalized value, never the raw one: equivalent spellings of the same document are the same request.
            var requestHash = IdempotencyRequestHash.Compute(emergencyId.ToString("D"), documentType.ToString(), documentNumber);

            if (await _receipts.FindAsync(key, cancellationToken) is { } existing)
            {
                return await ReplayAsync(actor, existing, requestHash, cancellationToken);
            }

            // Access to the emergency before the lookup: an unassigned user learns nothing about who holds a document.
            var emergency = await ReadAsync(actor, emergencyId, cancellationToken);
            var patientId = await _reads.FindPatientIdByPlatformIdentifierAsync(documentType, documentNumber, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(Patient), "No patient was found with this document.");

            // Tracked only: written by the SaveChangesAsync of IdentifyPatientAsync, in the same transaction.
            _receipts.Add(new IdempotencyReceipt(key, requestHash, emergency.Id, _time.GetUtcNow()));
            try
            {
                await _encounters.IdentifyPatientAsync(emergency.Id, new IdentifyEncounterPatientRequest(patientId), cancellationToken);
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

            return await ReadAsync(actor, emergency.Id, cancellationToken);
        }

        public async Task<PrehospitalEmergencySummary> GetAsync(Guid emergencyId, CancellationToken cancellationToken) =>
            await ReadAsync(await _actor.GetAsync(cancellationToken), emergencyId, cancellationToken);

        // The receipt's resource is the emergency it opened or identified, read again with the current access rules.
        private async Task<PrehospitalEmergencySummary> ReplayAsync(
            ClinicalActor actor, IdempotencyReceipt receipt, string requestHash, CancellationToken cancellationToken)
        {
            if (!string.Equals(receipt.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new ResourceConflictException(
                    "clientRequestId was already used with different data. Repeat the operation with a new request.");
            }

            return await ReadAsync(actor, receipt.ResourceId, cancellationToken);
        }

        // Only prehospital encounters are emergencies here; any other id is not found. Access: IEncounterAccessService
        // (assignment while there is no clinic), plus the patient rule once the patient is identified.
        private async Task<PrehospitalEmergencySummary> ReadAsync(ClinicalActor actor, Guid emergencyId, CancellationToken cancellationToken)
        {
            var encounter = await _reads.GetEncounterAsync(emergencyId, cancellationToken);
            if (encounter is null || encounter.Type != ClinicalEncounterType.Prehospital)
            {
                throw new ResourceNotFoundException("PrehospitalEmergency", emergencyId);
            }

            await _access.EnsureCanAccessAsync(actor, new EncounterAccessTarget(encounter.Id, encounter.Type, encounter.ClinicId), cancellationToken);
            if (encounter.PatientId is { } patientId
                && !await _patientAccess.CanReadAsync(patientId, Permissions.Encounters.Read, cancellationToken))
            {
                throw new AccessDeniedException("The current user cannot read this patient's encounters.");
            }

            return encounter.ToEmergency();
        }

        private static PatientIdentifierType DocumentType(PatientIdentifierType value) =>
            DocumentTypes.Contains(value)
                ? value
                : throw new RequestValidationException("documentType must be NationalId or Passport.", "documentType");

        // PatientIdentifier.Normalize, the same normalization as the stored identifiers. Errors never echo the value.
        private static string DocumentNumber(PatientIdentifierType type, string? value)
        {
            if (value is { Length: > FieldLengths.IdentifierValue })
            {
                throw new RequestValidationException(
                    $"documentNumber cannot exceed {FieldLengths.IdentifierValue} characters.", "documentNumber");
            }

            try
            {
                return PatientIdentifier.Normalize(type, value ?? string.Empty);
            }
            catch (InvalidPatientIdentifierException exception)
            {
                throw new RequestValidationException("documentNumber is required.", "documentNumber", exception);
            }
        }

        private static string ChiefComplaint(string? value)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                throw new RequestValidationException("chiefComplaint is required.", "chiefComplaint");
            }

            return text.Length <= FieldLengths.LongText
                ? text
                : throw new RequestValidationException($"chiefComplaint cannot exceed {FieldLengths.LongText} characters.", "chiefComplaint");
        }

        private static DateTimeOffset StartedAt(DateTimeOffset value, DateTimeOffset now)
        {
            if (value > now + MaxStartAhead)
            {
                throw new RequestValidationException("startedAt cannot be more than 5 minutes in the future.", "startedAt");
            }

            return value < now - MaxStartAge
                ? throw new RequestValidationException("startedAt cannot be more than 24 hours in the past.", "startedAt")
                : value.ToUniversalTime();
        }
    }
}
