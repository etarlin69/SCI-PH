using System.Globalization;
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
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Entities.Transport;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.Enums.Transport;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Application.Features.Clinical
{
    // Prehospital emergency opened by a paramedic from the mobile app: a Prehospital encounter WITHOUT a clinic (the
    // paramedic's base clinic is not where the emergency happens) and with an NN patient. The paramedic who opens it is
    // assigned to it (EncounterStaffAssignment, function Paramedic), which is what gives access to this concrete case
    // (IEncounterAccessService). On arrival, TransferService/ReceiveAtClinic sets the destination clinic on the SAME
    // encounter. The NN patient can then be identified by document on that same encounter (EncounterService), and readings
    // of vital signs, completed procedures, administered medications and observations are recorded on it, identified or
    // not. Opening, identifying and recording are idempotent per (user, operation, clientRequestId): see IIdempotencyStore.
    public sealed class PrehospitalEmergencyService
    {
        // Server-controlled operation name of the idempotency receipts; a new version gets a new name.
        public const string OpenOperation = "prehospital-emergency.open.v1";
        public const string IdentifyByDocumentOperation = "prehospital-emergency.identify-document.v1";
        public const string RecordVitalSignsOperation = "prehospital-emergency.vital-signs.v1";
        public const string RecordProcedureOperation = "prehospital-emergency.procedure.v1";
        public const string RecordMedicationAdministrationOperation = "prehospital-emergency.medication-administration.v1";
        public const string RecordNoteOperation = "prehospital-emergency.note.v1";
        public const string RequestTransferOperation = "prehospital-emergency.transfer-request.v1";

        // Statuses in which the paramedic records care in the field: also Transferred, which without clinic means on the
        // way to the hospital (an InTransit transfer). The Web keeps EncounterRecordService.RecordableStatuses.
        public static IReadOnlyCollection<ClinicalEncounterStatus> PrehospitalRecordableStatuses { get; } =
            [ClinicalEncounterStatus.Created, ClinicalEncounterStatus.InProgress, ClinicalEncounterStatus.Transferred];

        // VitalSign.Value is numeric(8,2): a value with more decimals is rejected rather than rounded by the database.
        public const int MaxVitalSignDecimals = 2;

        // MedicationAdministration.Dose is numeric(12,3): more decimals would be rounded by the database and 10^9 or more
        // would overflow it, so both are a 400 here instead.
        public const int MaxDoseDecimals = 3;
        public const decimal DoseUpperBound = 1_000_000_000m;

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
        // Like every mobile write, only before the handoff (LoadForIdentificationAsync): once a clinic received the
        // emergency, 409 for the paramedics who took part, also when affiliated with that clinic.
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
            await LoadForIdentificationAsync(actor, emergencyId, cancellationToken);
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

        // The emergency with its active transfer (Requested or InTransit), so the app can restore its transfer screen.
        public async Task<PrehospitalEmergencySummary> GetAsync(Guid emergencyId, CancellationToken cancellationToken)
        {
            var emergency = await ReadAsync(await _actor.GetAsync(cancellationToken), emergencyId, cancellationToken);
            return emergency with { ActiveTransfer = await ActiveTransferAsync(emergency.Id, cancellationToken) };
        }

        // Most emergencies one paramedic's active list returns.
        public const int ActiveEmergenciesLimit = 50;

        // The paramedic's actionable emergencies, newest first: the cases this user can still continue in the field, with
        // the same rule as every mobile record (LoadForRecordingAsync): prehospital, no clinic yet (once a clinic received
        // it, the handoff ended the paramedic's writes), an open assignment of the current staff member and a status of
        // PrehospitalRecordableStatuses (Transferred without clinic = on the way). The query enforces all of it; nothing
        // is filtered by the app. An identified case also needs the patient rule of the GET (ReadAsync); one that fails it
        // is left out rather than failing the whole list. Not active clinical staff: 403, as every mobile call.
        public async Task<IReadOnlyList<ActivePrehospitalEmergencySummary>> ListActiveAsync(CancellationToken cancellationToken)
        {
            var actor = await _actor.GetAsync(cancellationToken);
            var rows = await _reads.ListAssignedPrehospitalEmergenciesAsync(
                actor.StaffId, PrehospitalRecordableStatuses, ActiveEmergenciesLimit, cancellationToken);

            var result = new List<ActivePrehospitalEmergencySummary>(rows.Count);
            foreach (var (encounter, activeTransferStatus) in rows)
            {
                if (encounter.PatientId is { } patientId
                    && !await _patientAccess.CanReadAsync(patientId, Permissions.Encounters.Read, cancellationToken))
                {
                    continue;
                }

                result.Add(new ActivePrehospitalEmergencySummary(
                    encounter.Id,
                    encounter.TemporaryPatientCode,
                    encounter.Patient is { } patient ? ClinicalMapping.DisplayName(patient) : null,
                    encounter.Status,
                    encounter.StartedAtUtc,
                    encounter.Reason,
                    activeTransferStatus));
            }

            return result;
        }

        // Active clinics of the network a paramedic can take the patient to (no restriction by type). Only active clinical
        // staff use it (same actor rule as the other catalogs).
        public async Task<IReadOnlyList<TransferDestinationSummary>> ListTransferDestinationsAsync(CancellationToken cancellationToken)
        {
            await _actor.GetAsync(cancellationToken);
            return (await _reads.ListTransferDestinationsAsync(cancellationToken)).Select(d => d.ToSummary()).ToList();
        }

        // The paramedic decides where to take the patient: a Requested ClinicalTransfer without origin clinic (care outside a
        // clinic), its Create audit entry, the start of the encounter (Created -> InProgress: the request counts as the start
        // of the care) and the receipt, in one transaction. The time and the staff come from the server. The destination
        // must exist (404) and be active (409); the emergency must still be in the field, not already on its way (409), and
        // have no other active transfer (409, also enforced by a unique index). The destination clinic sees it among its
        // incoming transfers. Same key and same request again: the same transfer (in its current state); access is still
        // checked. Same key with another destination, reason, notes or emergency: 409.
        public async Task<RequestEmergencyTransferResult> RequestTransferAsync(
            Guid emergencyId, RequestEmergencyTransferRequest request, CancellationToken cancellationToken)
        {
            var clientRequestId = ClinicalInput.Id(request.ClientRequestId, "clientRequestId");
            var destinationId = ClinicalInput.Id(request.DestinationClinicId, "destinationClinicId");
            var reason = OptionalText(request.Reason, FieldLengths.ShortText, "reason");
            var notes = OptionalLongText(request.Notes, "notes");
            var actor = await _actor.GetAsync(cancellationToken);
            var key = new IdempotencyKey(actor.IdentityUserId, RequestTransferOperation, clientRequestId);
            var requestHash = IdempotencyRequestHash.Compute(emergencyId.ToString("D"), destinationId.ToString("D"), reason, notes);

            if (await _receipts.FindAsync(key, cancellationToken) is { } existing)
            {
                return await ReplayTransferRequestAsync(actor, existing, requestHash, emergencyId, cancellationToken);
            }

            var encounter = await LoadForPrehospitalWriteAsync(actor, emergencyId, Permissions.Transfers.Manage, cancellationToken);
            if (encounter.Status is not (ClinicalEncounterStatus.Created or ClinicalEncounterStatus.InProgress))
            {
                throw new ResourceConflictException($"An emergency in status {encounter.Status} cannot request a transfer.");
            }

            _writes.EnsureEncounterUnchanged(encounter);
            var destination = await _reads.GetTransferDestinationAsync(destinationId, cancellationToken)
                ?? throw new ResourceNotFoundException("Clinic", destinationId);
            if (destination.Status != ClinicStatus.Active)
            {
                throw new ResourceConflictException("The destination clinic is not active.");
            }

            if (await _reads.HasActiveTransferAsync(encounter.Id, cancellationToken))
            {
                // The same request may have committed it between the receipt lookup above and this check: then it is a replay.
                if (await _receipts.FindAsync(key, cancellationToken) is { } committed)
                {
                    return await ReplayTransferRequestAsync(actor, committed, requestHash, emergencyId, cancellationToken);
                }

                throw new ResourceConflictException("The emergency already has an active transfer.");
            }

            var now = _time.GetUtcNow();
            var transfer = ClinicalInput.Apply(() => ClinicalTransfer.Request(
                encounter.Id, destination.Id, now, originClinicId: null, reason: reason, notes: notes));
            StartCareIfPending(encounter, now);
            _writes.Add(transfer);
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Create, nameof(ClinicalTransfer), transfer.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id)));
            _receipts.Add(new IdempotencyReceipt(key, requestHash, transfer.Id, now));
            if (await SaveRecordAsync(key, cancellationToken) is { } winner)
            {
                return await ReplayTransferRequestAsync(actor, winner, requestHash, emergencyId, cancellationToken);
            }

            // Read back as stored, so the first answer, a replay and the emergency's activeTransfer are the same (the database
            // keeps microseconds).
            return new RequestEmergencyTransferResult(await TransferSummaryAsync(transfer.Id, cancellationToken), Created: true);
        }

        // The ambulance leaves with the patient (ClinicalTransfer.Dispatch: Requested -> InTransit, sent by the actor at the
        // server's time) and the encounter goes Transferred, in one transaction with its Transfer audit entry. Still without
        // clinic, so the paramedic keeps documenting on the way. Scoped to this transfer of this emergency (404 otherwise):
        // an InTransit transfer is a replay (200, nothing written); a cancelled, received or completed one is a 409.
        public async Task<EmergencyTransferSummary> StartTransferAsync(Guid emergencyId, Guid transferId, CancellationToken cancellationToken)
        {
            var actor = await _actor.GetAsync(cancellationToken);
            var (encounter, transfer) = await LoadTransferAsync(actor, emergencyId, transferId, cancellationToken);
            if (transfer.Status == ClinicalTransferStatus.InTransit)
            {
                return await TransferSummaryAsync(transfer.Id, cancellationToken);
            }

            if (transfer.Status != ClinicalTransferStatus.Requested)
            {
                throw new ResourceConflictException($"A {transfer.Status} transfer cannot be started.");
            }

            var now = _time.GetUtcNow();
            var previousStatus = encounter.Status;
            TransferConflict(() => transfer.Dispatch(actor.StaffId, now));
            TransferConflict(() => encounter.ChangeStatus(ClinicalEncounterStatus.Transferred, NotBeforeStart(encounter, now)));
            AddTransferAudit(actor, encounter, transfer, ClinicalTransferStatus.Requested, previousStatus, now);
            return await SaveTransferStepAsync(transfer.Id, ClinicalTransferStatus.InTransit, cancellationToken);
        }

        // The transfer is called off (ClinicalTransfer.Cancel at the server's time), so a wrong destination can be corrected
        // with a new request. Requested: only the transfer changes. InTransit: the encounter also returns Transferred ->
        // InProgress (the Domain transition of a cancelled transfer). Scoped to this transfer of this emergency (404
        // otherwise), never "the active one": a late retry for an older transfer cannot cancel a newer one. A cancelled
        // transfer is a replay (200, nothing written); a received or completed one is a 409.
        public async Task<EmergencyTransferSummary> CancelTransferAsync(Guid emergencyId, Guid transferId, CancellationToken cancellationToken)
        {
            var actor = await _actor.GetAsync(cancellationToken);
            var (encounter, transfer) = await LoadTransferAsync(actor, emergencyId, transferId, cancellationToken);
            if (transfer.Status == ClinicalTransferStatus.Cancelled)
            {
                return await TransferSummaryAsync(transfer.Id, cancellationToken);
            }

            if (transfer.Status is not (ClinicalTransferStatus.Requested or ClinicalTransferStatus.InTransit))
            {
                throw new ResourceConflictException($"A {transfer.Status} transfer cannot be cancelled.");
            }

            var now = _time.GetUtcNow();
            var previousTransfer = transfer.Status;
            var previousStatus = encounter.Status;
            TransferConflict(() => transfer.Cancel(now));
            if (previousTransfer == ClinicalTransferStatus.InTransit)
            {
                TransferConflict(() => encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, NotBeforeStart(encounter, now)));
            }
            else
            {
                // The encounter does not change, but a concurrent handoff or identification must still win cleanly.
                _writes.EnsureEncounterUnchanged(encounter);
            }

            AddTransferAudit(actor, encounter, transfer, previousTransfer, previousStatus, now);
            return await SaveTransferStepAsync(transfer.Id, ClinicalTransferStatus.Cancelled, cancellationToken);
        }

        // The emergency (prehospital write rule, Transfers.Manage) and THIS transfer of it; a transfer of another emergency
        // is reported as not found.
        private async Task<(ClinicalEncounter Encounter, ClinicalTransfer Transfer)> LoadTransferAsync(
            ClinicalActor actor, Guid emergencyId, Guid transferId, CancellationToken cancellationToken)
        {
            var encounter = await LoadForPrehospitalWriteAsync(actor, emergencyId, Permissions.Transfers.Manage, cancellationToken);
            var transfer = await _writes.FindTransferAsync(transferId, cancellationToken);
            if (transfer is null || transfer.ClinicalEncounterId != encounter.Id)
            {
                throw new ResourceNotFoundException(nameof(ClinicalTransfer), transferId);
            }

            return (encounter, transfer);
        }

        // Same audit shape as the Web transfer steps: the transfer and encounter status before and after.
        private void AddTransferAudit(
            ClinicalActor actor, ClinicalEncounter encounter, ClinicalTransfer transfer, ClinicalTransferStatus previousTransfer,
            ClinicalEncounterStatus previousStatus, DateTimeOffset now) =>
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Transfer, nameof(ClinicalTransfer), transfer.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id, ChangesJson: JsonSerializer.Serialize(new
                {
                    transfer = new[] { previousTransfer.ToString(), transfer.Status.ToString() },
                    encounterStatus = new[] { previousStatus.ToString(), encounter.Status.ToString() }
                }))));

        // Saves a transfer step. Lost a race (the transfer or the encounter changed meanwhile): a replay when the transfer
        // already is in the target status (the same step won), otherwise the 409.
        private async Task<EmergencyTransferSummary> SaveTransferStepAsync(
            Guid transferId, ClinicalTransferStatus target, CancellationToken cancellationToken)
        {
            try
            {
                await _writes.SaveChangesAsync(cancellationToken);
            }
            catch (ResourceConflictException)
            {
                if ((await _reads.GetTransferAsync(transferId, cancellationToken))?.Status != target)
                {
                    throw;
                }
            }

            return await TransferSummaryAsync(transferId, cancellationToken);
        }

        // The receipt's resource is the requested transfer, returned in its current state; access is checked again.
        private async Task<RequestEmergencyTransferResult> ReplayTransferRequestAsync(
            ClinicalActor actor, IdempotencyReceipt receipt, string requestHash, Guid emergencyId, CancellationToken cancellationToken)
        {
            EnsureSameRequest(receipt, requestHash);
            await ReadAsync(actor, emergencyId, cancellationToken);
            return new RequestEmergencyTransferResult(await TransferSummaryAsync(receipt.ResourceId, cancellationToken), Created: false);
        }

        private async Task<EmergencyTransferSummary?> ActiveTransferAsync(Guid encounterId, CancellationToken cancellationToken) =>
            await _reads.FindActiveTransferIdAsync(encounterId, cancellationToken) is { } transferId
                ? await TransferSummaryAsync(transferId, cancellationToken)
                : null;

        private async Task<EmergencyTransferSummary> TransferSummaryAsync(Guid transferId, CancellationToken cancellationToken)
        {
            var row = await _reads.GetTransferAsync(transferId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(ClinicalTransfer), transferId);
            var destination = await _reads.GetTransferDestinationAsync(row.DestinationClinic.Id, cancellationToken)
                ?? throw new InvalidOperationException("The destination clinic of a transfer was not found.");
            return new EmergencyTransferSummary(
                row.Id, row.EncounterId, row.Status, destination.ToSummary(), row.RequestedAtUtc, row.SentAtUtc, row.CancelledAtUtc);
        }

        // Transfer and encounter state rules hit by a valid request are conflicts with the current state (409).
        private static void TransferConflict(Action domainCall)
        {
            try
            {
                domainCall();
            }
            catch (Exception exception) when (exception is InvalidClinicalTransferException or InvalidClinicalEncounterStateException)
            {
                throw new ResourceConflictException(exception.Message, exception);
            }
        }

        // A server timestamp for a status change, never before the start (the device clock may have started it ahead).
        private static DateTimeOffset NotBeforeStart(ClinicalEncounter encounter, DateTimeOffset now) =>
            now > encounter.StartedAtUtc ? now : encounter.StartedAtUtc;

        // One reading of 1 to 7 vital signs, all or nothing: the measurements, one audit entry per measurement, the start of
        // the encounter (Created -> InProgress, as the first Web record does) and the receipt, in one transaction. Works on
        // an NN or an identified emergency. Access: the assignment while there is no clinic (IEncounterAccessService); the
        // shared recording check of EncounterRecordService is not used, so medications keep the clinic rule.
        // recordedAt: from the start of the emergency up to 5 minutes after the server's clock (the device skew already
        // accepted for startedAt), both inclusive; the Web keeps its strict rule. Same key and same reading again (also when
        // it races the first one): the same answer, nothing new, even if the clock or the status moved since; access is
        // still checked. Same key with another reading or emergency: 409.
        public async Task<RecordEmergencyVitalSignsResult> RecordVitalSignsAsync(
            Guid emergencyId, RecordEmergencyVitalSignsRequest request, CancellationToken cancellationToken)
        {
            var clientRequestId = ClinicalInput.Id(request.ClientRequestId, "clientRequestId");
            var recordedAt = request.RecordedAt.ToUniversalTime();
            var measurements = VitalSignMeasurements(request);
            var actor = await _actor.GetAsync(cancellationToken);
            var key = new IdempotencyKey(actor.IdentityUserId, RecordVitalSignsOperation, clientRequestId);
            // Fixed type order and the numeric value (37.5 and 37.50 are the same reading), never the raw JSON.
            var requestHash = IdempotencyRequestHash.Compute(
            [
                emergencyId.ToString("D"),
                IdempotencyRequestHash.Instant(recordedAt),
                .. measurements.SelectMany(m => new[] { m.Type.ToString(), m.Value.ToString(CultureInfo.InvariantCulture) })
            ]);

            if (await _receipts.FindAsync(key, cancellationToken) is { } existing)
            {
                return await ReplayVitalSignsAsync(actor, existing, requestHash, recordedAt, measurements, cancellationToken);
            }

            var encounter = await LoadForRecordingAsync(actor, emergencyId, Permissions.VitalSigns.Write, cancellationToken);
            var now = _time.GetUtcNow();
            DeviceClinicalTime(recordedAt, encounter.StartedAtUtc, now, "recordedAt");
            var vitalSigns = measurements
                .Select(m => ClinicalInput.Apply(() => VitalSign.Record(encounter.Id, m.Type, m.Value, recordedAt, actor.StaffId)))
                .ToList();

            StartCareIfPending(encounter, now);
            foreach (var vitalSign in vitalSigns)
            {
                _writes.Add(vitalSign);
                _writes.Add(ClinicalAuditEntry.Success(
                    actor.AuditActor, AuditActionType.Create, nameof(VitalSign), vitalSign.Id.ToString(), now,
                    new AuditContext(encounter.PatientId, encounter.Id)));
            }

            _receipts.Add(new IdempotencyReceipt(key, requestHash, encounter.Id, now));
            if (await SaveRecordAsync(key, cancellationToken) is { } winner)
            {
                return await ReplayVitalSignsAsync(actor, winner, requestHash, recordedAt, measurements, cancellationToken);
            }

            return new RecordEmergencyVitalSignsResult(
                new EmergencyVitalSignsSummary(encounter.Id, recordedAt, measurements), Created: true);
        }

        // The fixed prehospital procedure catalog. Only active clinical staff use it (same actor rule as every clinical
        // endpoint, like the medication catalog); it holds no patient data.
        public async Task<IReadOnlyList<PrehospitalProcedureCatalogItem>> ListProcedureCatalogAsync(CancellationToken cancellationToken)
        {
            await _actor.GetAsync(cancellationToken);
            return PrehospitalProcedureCatalog.Entries;
        }

        // A procedure the paramedic already performed (ClinicalProcedure.RecordCompleted: performed and completed at the
        // single instant performedAt), with its audit entry, the start of the encounter (Created -> InProgress, as the first
        // reading of vital signs does) and the receipt, in one transaction. The name, description and code come from the
        // server's catalog; outcome is the paramedic's free text with the result. Access, statuses, the clock window and
        // the encounter's concurrency tokens are the same as for vital signs. Same key and same procedure again (also when
        // it races the first one): the same answer, nothing new; access is still checked. Same key with another procedure,
        // time, outcome or emergency: 409.
        public async Task<RecordEmergencyProcedureResult> RecordProcedureAsync(
            Guid emergencyId, RecordEmergencyProcedureRequest request, CancellationToken cancellationToken)
        {
            var clientRequestId = ClinicalInput.Id(request.ClientRequestId, "clientRequestId");
            var entry = ProcedureCode(request.ProcedureCode);
            var performedAt = request.PerformedAt.ToUniversalTime();
            var outcome = Outcome(request.Outcome);
            var actor = await _actor.GetAsync(cancellationToken);
            var key = new IdempotencyKey(actor.IdentityUserId, RecordProcedureOperation, clientRequestId);
            // The catalog code, the instant in UTC and the trimmed outcome (blank = none), never the raw JSON.
            var requestHash = IdempotencyRequestHash.Compute(
                emergencyId.ToString("D"), entry.Code, IdempotencyRequestHash.Instant(performedAt), outcome);

            if (await _receipts.FindAsync(key, cancellationToken) is { } existing)
            {
                return await ReplayProcedureAsync(actor, existing, requestHash, emergencyId, entry, performedAt, outcome, cancellationToken);
            }

            var encounter = await LoadForRecordingAsync(actor, emergencyId, Permissions.Procedures.Write, cancellationToken);
            var now = _time.GetUtcNow();
            DeviceClinicalTime(performedAt, encounter.StartedAtUtc, now, "performedAt");
            var procedure = ClinicalInput.Apply(() => ClinicalProcedure.RecordCompleted(
                encounter.Id, entry.Name, actor.StaffId, performedAt, performedAt, outcome, entry.Description,
                PrehospitalProcedureCatalog.CodeSystem, entry.Code));

            StartCareIfPending(encounter, now);
            _writes.Add(procedure);
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Create, nameof(ClinicalProcedure), procedure.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id)));
            _receipts.Add(new IdempotencyReceipt(key, requestHash, procedure.Id, now));
            if (await SaveRecordAsync(key, cancellationToken) is { } winner)
            {
                return await ReplayProcedureAsync(actor, winner, requestHash, emergencyId, entry, performedAt, outcome, cancellationToken);
            }

            return new RecordEmergencyProcedureResult(
                new EmergencyProcedureSummary(procedure.Id, encounter.Id, entry.Code, entry.Name, performedAt, outcome), Created: true);
        }

        // The active entries of the global medication catalog, by name and concentration. Only active clinical staff use it
        // (same actor rule as the Web catalog). An entry deactivated after the app loaded the list is still rejected by
        // RecordMedicationAdministrationAsync.
        public async Task<IReadOnlyList<PrehospitalMedicationCatalogItem>> ListMedicationCatalogAsync(CancellationToken cancellationToken)
        {
            await _actor.GetAsync(cancellationToken);
            return (await _reads.ListMedicationsAsync(cancellationToken))
                .Where(m => m.Status == MedicationStatus.Active)
                .Select(m => new PrehospitalMedicationCatalogItem(m.Id, m.Code, m.Name, m.Presentation, m.Concentration, m.DefaultRoute))
                .ToList();
        }

        // A dose the paramedic already gave (MedicationAdministration.Record, without prescription: the paramedic does not
        // prescribe), with its audit entry, the start of the encounter (Created -> InProgress) and the receipt, in one
        // transaction. The medication must exist (404) and be active (409); the staff comes from the session. Access,
        // statuses, the clock window and the encounter's concurrency tokens are the same as for vital signs and procedures;
        // the Web path (MedicationService) keeps the clinic rule. Same key and same dose again (also when it races the first
        // one): the same answer, nothing new, even if the medication was deactivated since; access is still checked. Same
        // key with any other value or emergency: 409.
        public async Task<RecordEmergencyMedicationAdministrationResult> RecordMedicationAdministrationAsync(
            Guid emergencyId, RecordEmergencyMedicationAdministrationRequest request, CancellationToken cancellationToken)
        {
            var clientRequestId = ClinicalInput.Id(request.ClientRequestId, "clientRequestId");
            ClinicalInput.Id(request.MedicationId, "medicationId");
            var dose = Dose(request.Dose);
            var doseUnit = DoseUnit(request.DoseUnit);
            var route = ClinicalInput.Defined(request.Route, "route");
            var administeredAt = request.AdministeredAt.ToUniversalTime();
            var notes = OptionalLongText(request.Notes, "notes");
            var actor = await _actor.GetAsync(cancellationToken);
            var key = new IdempotencyKey(actor.IdentityUserId, RecordMedicationAdministrationOperation, clientRequestId);
            // Canonical values: the dose without trailing zeros (500 = 500.0), the trimmed unit, the route name, the
            // instant in UTC and the trimmed notes (blank = none). Never the raw JSON.
            var requestHash = IdempotencyRequestHash.Compute(
                emergencyId.ToString("D"), request.MedicationId.ToString("D"), dose.ToString(CultureInfo.InvariantCulture), doseUnit,
                route.ToString(), IdempotencyRequestHash.Instant(administeredAt), notes);

            if (await _receipts.FindAsync(key, cancellationToken) is { } existing)
            {
                return await ReplayMedicationAdministrationAsync(
                    actor, existing, requestHash, emergencyId, request.MedicationId, dose, doseUnit, route, administeredAt, notes, cancellationToken);
            }

            var encounter = await LoadForRecordingAsync(actor, emergencyId, Permissions.MedicationAdministrations.Write, cancellationToken);
            var now = _time.GetUtcNow();
            DeviceClinicalTime(administeredAt, encounter.StartedAtUtc, now, "administeredAt");
            var medication = (await MedicationService.LoadActiveMedicationsAsync(_reads, [request.MedicationId], cancellationToken))[request.MedicationId];
            var administration = ClinicalInput.Apply(() => MedicationAdministration.Record(
                encounter.Id, medication.Id, dose, doseUnit, route, administeredAt, actor.StaffId,
                prescriptionId: null, prescriptionDetailId: null, notes: notes));

            StartCareIfPending(encounter, now);
            _writes.Add(administration);
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Create, nameof(MedicationAdministration), administration.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id)));
            _receipts.Add(new IdempotencyReceipt(key, requestHash, administration.Id, now));
            if (await SaveRecordAsync(key, cancellationToken) is { } winner)
            {
                return await ReplayMedicationAdministrationAsync(
                    actor, winner, requestHash, emergencyId, request.MedicationId, dose, doseUnit, route, administeredAt, notes, cancellationToken);
            }

            return new RecordEmergencyMedicationAdministrationResult(
                new EmergencyMedicationAdministrationSummary(
                    administration.Id, encounter.Id, medication.Id, medication.Name, dose, doseUnit, route, administeredAt, notes),
                Created: true);
        }

        // An observation of the paramedic (ClinicalNote.Write, type always Prehospital, author = the session's staff), with its
        // audit entry (never the content), the start of the encounter (Created -> InProgress) and the receipt, in one
        // transaction. Notes are immutable: a correction is another note. Access, statuses, the clock window and the
        // encounter's concurrency tokens are the same as for the other records; the Web path (EncounterRecordService) keeps
        // the clinic rule. Same key and same note again (also when it races the first one): the same answer, nothing new;
        // access is still checked. Same key with another content, time or emergency: 409.
        public async Task<RecordEmergencyNoteResult> RecordNoteAsync(
            Guid emergencyId, RecordEmergencyNoteRequest request, CancellationToken cancellationToken)
        {
            var clientRequestId = ClinicalInput.Id(request.ClientRequestId, "clientRequestId");
            var occurredAt = request.OccurredAt.ToUniversalTime();
            var content = NoteContent(request.Content);
            var actor = await _actor.GetAsync(cancellationToken);
            var key = new IdempotencyKey(actor.IdentityUserId, RecordNoteOperation, clientRequestId);
            // The instant in UTC and the trimmed content, never the raw JSON; CreatedAtUtc is not part of the request.
            var requestHash = IdempotencyRequestHash.Compute(
                emergencyId.ToString("D"), IdempotencyRequestHash.Instant(occurredAt), content);

            if (await _receipts.FindAsync(key, cancellationToken) is { } existing)
            {
                return await ReplayNoteAsync(actor, existing, requestHash, emergencyId, occurredAt, content, cancellationToken);
            }

            var encounter = await LoadForRecordingAsync(actor, emergencyId, Permissions.ClinicalNotes.Write, cancellationToken);
            var now = _time.GetUtcNow();
            DeviceClinicalTime(occurredAt, encounter.StartedAtUtc, now, "occurredAt");
            var note = ClinicalInput.Apply(() => ClinicalNote.Write(
                encounter.Id, actor.StaffId, ClinicalNoteType.Prehospital, content, occurredAt));

            StartCareIfPending(encounter, now);
            _writes.Add(note);
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Create, nameof(ClinicalNote), note.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id)));
            _receipts.Add(new IdempotencyReceipt(key, requestHash, note.Id, now));
            if (await SaveRecordAsync(key, cancellationToken) is { } winner)
            {
                return await ReplayNoteAsync(actor, winner, requestHash, emergencyId, occurredAt, content, cancellationToken);
            }

            return new RecordEmergencyNoteResult(
                new EmergencyNoteSummary(note.Id, encounter.Id, ClinicalNoteType.Prehospital, content, occurredAt), Created: true);
        }

        // Shared write check of the records on an emergency (vital signs, procedures, medication administrations, notes):
        // the prehospital write rule (LoadForPrehospitalWriteAsync) and the statuses of the care in the field, Transferred
        // included (on the way to the hospital, still without clinic); the shared recording check of EncounterRecordService
        // is not used, so the Web records keep their rules. The status check can be outdated by commit time, so the
        // encounter's concurrency tokens (Status, PatientId) are re-checked inside the save: a concurrent close, dispatch,
        // reception or identification makes the record lose with a 409.
        private async Task<ClinicalEncounter> LoadForRecordingAsync(
            ClinicalActor actor, Guid emergencyId, string permission, CancellationToken cancellationToken)
        {
            var encounter = await LoadForPrehospitalWriteAsync(actor, emergencyId, permission, cancellationToken);
            if (!PrehospitalRecordableStatuses.Contains(encounter.Status))
            {
                throw new ResourceConflictException($"An encounter in status {encounter.Status} does not accept clinical records.");
            }

            _writes.EnsureEncounterUnchanged(encounter);
            return encounter;
        }

        // Every prehospital write of the mobile app (records and transfer steps). Only prehospital encounters are
        // emergencies here (404 otherwise). The care in the field ends at the handoff: once a clinic received the emergency
        // (ClinicId set by ReceiveAtClinic), no mobile write is accepted, even from a paramedic also affiliated with that
        // clinic: a 409 for the paramedics who took part, the 403 of a foreign case for anyone else. While there is no
        // clinic: the assignment (IEncounterAccessService), plus the patient rule for the permission once the patient is
        // identified.
        private async Task<ClinicalEncounter> LoadForPrehospitalWriteAsync(
            ClinicalActor actor, Guid emergencyId, string permission, CancellationToken cancellationToken)
        {
            var encounter = await LoadForIdentificationAsync(actor, emergencyId, cancellationToken);
            if (encounter.PatientId is { } patientId && !await _patientAccess.CanWriteAsync(patientId, permission, cancellationToken))
            {
                throw new AccessDeniedException("The current user cannot write this patient's clinical records.");
            }

            return encounter;
        }

        // The prehospital write rule without the patient rule, as the identification of the NN patient needs it (by document
        // or by fingerprint, see EmergencyFingerprintService): 404 for anything but a prehospital encounter; once a clinic
        // received it, 409 for the paramedics who took part and 403 for anyone else; before, the assignment. The encounter
        // is TRACKED, so EncounterService.IdentifyPatientAsync later works on this same instance and its original Status
        // and PatientId: a reception or identification committed after this check makes the identification lose (409).
        internal async Task<ClinicalEncounter> LoadForIdentificationAsync(
            ClinicalActor actor, Guid emergencyId, CancellationToken cancellationToken)
        {
            var encounter = await _writes.FindEncounterAsync(emergencyId, cancellationToken);
            if (encounter is null || encounter.Type != ClinicalEncounterType.Prehospital)
            {
                throw new ResourceNotFoundException("PrehospitalEmergency", emergencyId);
            }

            if (encounter.ClinicId is not null)
            {
                if (await _reads.HasActiveStaffAssignmentAsync(encounter.Id, actor.StaffId, cancellationToken))
                {
                    throw new ResourceConflictException(
                        "The emergency was handed off to the receiving clinic; it no longer accepts prehospital records.");
                }

                throw new AccessDeniedException("The current user is not assigned to this prehospital encounter.");
            }

            await _access.EnsureCanAccessAsync(
                actor, new EncounterAccessTarget(encounter.Id, encounter.Type, encounter.ClinicId), cancellationToken);
            return encounter;
        }

        // The Domain never changes status before the start. An emergency opened with the device clock ahead (up to
        // 5 minutes, accepted) starts "after" the server's now, so the transition time, a server timestamp, is not
        // earlier than the start. The device's time of the record is never changed. In the field only Created -> InProgress
        // applies: a Received emergency already has a clinic and accepts no mobile record (LoadForIdentificationAsync).
        private static void StartCareIfPending(ClinicalEncounter encounter, DateTimeOffset now) =>
            EncounterRecordService.StartCareIfPending(encounter, NotBeforeStart(encounter, now));

        // Saves the tracked record and its receipt. Returns the receipt of the request that committed instead when this one
        // lost: a concurrent request with the same key committed first (this one wrote nothing), or the encounter changed
        // meanwhile and the same request is the one that committed. Any other conflict is rethrown (409).
        private async Task<IdempotencyReceipt?> SaveRecordAsync(IdempotencyKey key, CancellationToken cancellationToken)
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

        // The receipt's resource is the recorded procedure; the emergency is the one of the request (the hash covers it).
        // A replay re-checks access (ReadAsync) but not the clock or the status: the procedure was valid when recorded.
        private async Task<RecordEmergencyProcedureResult> ReplayProcedureAsync(
            ClinicalActor actor,
            IdempotencyReceipt receipt,
            string requestHash,
            Guid emergencyId,
            PrehospitalProcedureCatalogItem entry,
            DateTimeOffset performedAt,
            string? outcome,
            CancellationToken cancellationToken)
        {
            EnsureSameRequest(receipt, requestHash);
            var emergency = await ReadAsync(actor, emergencyId, cancellationToken);
            return new RecordEmergencyProcedureResult(
                new EmergencyProcedureSummary(receipt.ResourceId, emergency.Id, entry.Code, entry.Name, performedAt, outcome), Created: false);
        }

        // The receipt's resource is the recorded administration; the emergency and the medication are the ones of the request
        // (the hash covers them). A replay re-checks access (ReadAsync) but not the clock, the status or whether the
        // medication is still active: the dose was valid when it was recorded. Only its name is read again.
        private async Task<RecordEmergencyMedicationAdministrationResult> ReplayMedicationAdministrationAsync(
            ClinicalActor actor,
            IdempotencyReceipt receipt,
            string requestHash,
            Guid emergencyId,
            Guid medicationId,
            decimal dose,
            string doseUnit,
            MedicationRoute route,
            DateTimeOffset administeredAt,
            string? notes,
            CancellationToken cancellationToken)
        {
            EnsureSameRequest(receipt, requestHash);
            var emergency = await ReadAsync(actor, emergencyId, cancellationToken);
            var medication = (await _reads.FindMedicationsAsync([medicationId], cancellationToken)).SingleOrDefault()
                ?? throw new InvalidOperationException("The medication of a recorded administration was not found.");
            return new RecordEmergencyMedicationAdministrationResult(
                new EmergencyMedicationAdministrationSummary(
                    receipt.ResourceId, emergency.Id, medication.Id, medication.Name, dose, doseUnit, route, administeredAt, notes),
                Created: false);
        }

        // The receipt's resource is the recorded note; the emergency is the one of the request (the hash covers it). A replay
        // re-checks access (ReadAsync) but not the clock or the status: the note was valid when it was recorded.
        private async Task<RecordEmergencyNoteResult> ReplayNoteAsync(
            ClinicalActor actor,
            IdempotencyReceipt receipt,
            string requestHash,
            Guid emergencyId,
            DateTimeOffset occurredAt,
            string content,
            CancellationToken cancellationToken)
        {
            EnsureSameRequest(receipt, requestHash);
            var emergency = await ReadAsync(actor, emergencyId, cancellationToken);
            return new RecordEmergencyNoteResult(
                new EmergencyNoteSummary(receipt.ResourceId, emergency.Id, ClinicalNoteType.Prehospital, content, occurredAt), Created: false);
        }

        // A replay re-checks access (ReadAsync) but not the clock or the status: the reading was valid when it was recorded.
        private async Task<RecordEmergencyVitalSignsResult> ReplayVitalSignsAsync(
            ClinicalActor actor,
            IdempotencyReceipt receipt,
            string requestHash,
            DateTimeOffset recordedAt,
            IReadOnlyList<EmergencyVitalSignMeasurement> measurements,
            CancellationToken cancellationToken)
        {
            var emergency = await ReplayAsync(actor, receipt, requestHash, cancellationToken);
            return new RecordEmergencyVitalSignsResult(new EmergencyVitalSignsSummary(emergency.Id, recordedAt, measurements), Created: false);
        }

        // The receipt's resource is the emergency it opened, identified or recorded vital signs on, read again with the
        // current access rules.
        private async Task<PrehospitalEmergencySummary> ReplayAsync(
            ClinicalActor actor, IdempotencyReceipt receipt, string requestHash, CancellationToken cancellationToken)
        {
            EnsureSameRequest(receipt, requestHash);
            return await ReadAsync(actor, receipt.ResourceId, cancellationToken);
        }

        internal static void EnsureSameRequest(IdempotencyReceipt receipt, string requestHash)
        {
            if (!string.Equals(receipt.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new ResourceConflictException(
                    "clientRequestId was already used with different data. Repeat the operation with a new request.");
            }
        }

        // Only prehospital encounters are emergencies here; any other id is not found. Access: IEncounterAccessService
        // (assignment while there is no clinic), plus the patient rule once the patient is identified.
        internal async Task<PrehospitalEmergencySummary> ReadAsync(ClinicalActor actor, Guid emergencyId, CancellationToken cancellationToken)
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

        // The supplied values in a fixed type order, each checked against the server's plausibility limits (no clinical
        // rule) and returned in canonical form (37.50 -> 37.5) with the server-owned unit.
        private static IReadOnlyList<EmergencyVitalSignMeasurement> VitalSignMeasurements(RecordEmergencyVitalSignsRequest request)
        {
            (VitalSignType Type, string Field, decimal? Value)[] supplied =
            [
                (VitalSignType.HeartRate, "heartRate", request.HeartRate),
                (VitalSignType.RespiratoryRate, "respiratoryRate", request.RespiratoryRate),
                (VitalSignType.SystolicBloodPressure, "systolicPressure", request.SystolicPressure),
                (VitalSignType.DiastolicBloodPressure, "diastolicPressure", request.DiastolicPressure),
                (VitalSignType.Temperature, "temperature", request.Temperature),
                (VitalSignType.OxygenSaturation, "oxygenSaturation", request.OxygenSaturation),
                (VitalSignType.GlasgowComaScale, "glasgow", request.Glasgow)
            ];

            var measurements = new List<EmergencyVitalSignMeasurement>();
            foreach (var (type, field, value) in supplied.Where(s => s.Value is not null).OrderBy(s => s.Type))
            {
                var limit = VitalSignLimits.For(type);
                var number = value!.Value;
                if (decimal.Round(number, MaxVitalSignDecimals) != number)
                {
                    throw new RequestValidationException($"{field} accepts at most {MaxVitalSignDecimals} decimals.", field);
                }

                if (number < limit.Minimum || number > limit.Maximum)
                {
                    throw new RequestValidationException(
                        $"{field} must be between {limit.Minimum} and {limit.Maximum} {limit.Unit}.", field);
                }

                // Dividing by 1.000… drops trailing zeros without changing the value.
                measurements.Add(new EmergencyVitalSignMeasurement(type, number / 1.0000000000000000000000000000m, limit.Unit));
            }

            return measurements.Count > 0
                ? measurements
                : throw new RequestValidationException("A reading needs at least one vital sign.");
        }

        // The device's time of a record: from the start of the emergency up to 5 minutes after the server's clock (the
        // device skew already accepted for startedAt), both inclusive. The Web keeps its strict no-future rule.
        private static void DeviceClinicalTime(DateTimeOffset value, DateTimeOffset encounterStartedAtUtc, DateTimeOffset now, string field)
        {
            if (value < encounterStartedAtUtc)
            {
                throw new RequestValidationException($"{field} cannot be earlier than the start of the emergency.", field);
            }

            if (value > now + MaxStartAhead)
            {
                throw new RequestValidationException($"{field} cannot be more than 5 minutes in the future.", field);
            }
        }

        // The catalog entry of the code, matched exactly. A code outside the catalog is a 400: there is no free-text procedure.
        private static PrehospitalProcedureCatalogItem ProcedureCode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new RequestValidationException("procedureCode is required.", "procedureCode");
            }

            return PrehospitalProcedureCatalog.Find(value)
                ?? throw new RequestValidationException("procedureCode is not in the prehospital procedure catalog.", "procedureCode");
        }

        // ClinicalProcedure.Outcome: trimmed, blank = none, at most FieldLengths.LongText characters. Never echoed in errors.
        private static string? Outcome(string? value) => OptionalLongText(value, "outcome");

        // Optional free text of a record (procedure outcome, administration notes): trimmed, blank = none, at most
        // FieldLengths.LongText characters. Never echoed in errors.
        private static string? OptionalLongText(string? value, string field) => OptionalText(value, FieldLengths.LongText, field);

        // Optional free text: trimmed, blank = none, at most maxLength characters. Never echoed in errors.
        private static string? OptionalText(string? value, int maxLength, string field)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            return text.Length <= maxLength
                ? text
                : throw new RequestValidationException($"{field} cannot exceed {maxLength} characters.", field);
        }

        // ClinicalNote.Content: required, trimmed, 1 to FieldLengths.NoteContent characters. Never echoed in errors.
        private static string NoteContent(string? value)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                throw new RequestValidationException("content is required.", "content");
            }

            return text.Length <= FieldLengths.NoteContent
                ? text
                : throw new RequestValidationException($"content cannot exceed {FieldLengths.NoteContent} characters.", "content");
        }

        // MedicationAdministration.Dose: > 0 (Domain), at most 3 decimals and below 10^9 (numeric(12,3)), in canonical form
        // (500.0 -> 500), so the database neither rounds nor overflows it. No clinical dose rule.
        private static decimal Dose(decimal value)
        {
            if (value <= 0)
            {
                throw new RequestValidationException("dose must be greater than zero.", "dose");
            }

            if (decimal.Round(value, MaxDoseDecimals) != value)
            {
                throw new RequestValidationException($"dose accepts at most {MaxDoseDecimals} decimals.", "dose");
            }

            return value < DoseUpperBound
                ? value / 1.0000000000000000000000000000m
                : throw new RequestValidationException("dose must be less than 1000000000.", "dose");
        }

        // MedicationAdministration.DoseUnit: free text, trimmed, 1 to FieldLengths.MeasurementUnit characters. Kept as written
        // (no unit catalog, no conversion).
        private static string DoseUnit(string? value)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                throw new RequestValidationException("doseUnit is required.", "doseUnit");
            }

            return text.Length <= FieldLengths.MeasurementUnit
                ? text
                : throw new RequestValidationException($"doseUnit cannot exceed {FieldLengths.MeasurementUnit} characters.", "doseUnit");
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
