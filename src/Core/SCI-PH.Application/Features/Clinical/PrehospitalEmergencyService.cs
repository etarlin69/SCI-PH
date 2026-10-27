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
    // encounter. The NN patient can then be identified by document on that same encounter (EncounterService), and readings
    // of vital signs and completed procedures are recorded on it, identified or not. Opening, identifying and recording are
    // idempotent per (user, operation, clientRequestId): see IIdempotencyStore.
    public sealed class PrehospitalEmergencyService
    {
        // Server-controlled operation name of the idempotency receipts; a new version gets a new name.
        public const string OpenOperation = "prehospital-emergency.open.v1";
        public const string IdentifyByDocumentOperation = "prehospital-emergency.identify-document.v1";
        public const string RecordVitalSignsOperation = "prehospital-emergency.vital-signs.v1";
        public const string RecordProcedureOperation = "prehospital-emergency.procedure.v1";

        // VitalSign.Value is numeric(8,2): a value with more decimals is rejected rather than rounded by the database.
        public const int MaxVitalSignDecimals = 2;

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

            StartIfCreated(encounter, now);
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

            StartIfCreated(encounter, now);
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

        // Shared write check of the records on an emergency (vital signs, procedures). Only prehospital encounters are
        // emergencies here. Access: the assignment while there is no clinic (IEncounterAccessService), plus the patient
        // rule for the permission once the patient is identified; the shared recording check of EncounterRecordService is
        // not used, so the Web records keep the clinic rule. The status check can be outdated by commit time, so the
        // encounter's concurrency tokens (Status, PatientId) are re-checked inside the save: a concurrent close, dispatch
        // or identification makes the record lose with a 409.
        private async Task<ClinicalEncounter> LoadForRecordingAsync(
            ClinicalActor actor, Guid emergencyId, string permission, CancellationToken cancellationToken)
        {
            var encounter = await _writes.FindEncounterAsync(emergencyId, cancellationToken);
            if (encounter is null || encounter.Type != ClinicalEncounterType.Prehospital)
            {
                throw new ResourceNotFoundException("PrehospitalEmergency", emergencyId);
            }

            await _access.EnsureCanAccessAsync(
                actor, new EncounterAccessTarget(encounter.Id, encounter.Type, encounter.ClinicId), cancellationToken);
            if (encounter.PatientId is { } patientId && !await _patientAccess.CanWriteAsync(patientId, permission, cancellationToken))
            {
                throw new AccessDeniedException("The current user cannot write this patient's clinical records.");
            }

            if (!EncounterRecordService.RecordableStatuses.Contains(encounter.Status))
            {
                throw new ResourceConflictException($"An encounter in status {encounter.Status} does not accept clinical records.");
            }

            _writes.EnsureEncounterUnchanged(encounter);
            return encounter;
        }

        // The Domain never changes status before the start. An emergency opened with the device clock ahead (up to
        // 5 minutes, accepted) starts "after" the server's now, so the transition time, a server timestamp, is not
        // earlier than the start. The device's time of the record is never changed.
        private static void StartIfCreated(ClinicalEncounter encounter, DateTimeOffset now) =>
            EncounterRecordService.StartIfCreated(encounter, now > encounter.StartedAtUtc ? now : encounter.StartedAtUtc);

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

        private static void EnsureSameRequest(IdempotencyReceipt receipt, string requestHash)
        {
            if (!string.Equals(receipt.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new ResourceConflictException(
                    "clientRequestId was already used with different data. Repeat the operation with a new request.");
            }
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
        private static string? Outcome(string? value)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            return text.Length <= FieldLengths.LongText
                ? text
                : throw new RequestValidationException($"outcome cannot exceed {FieldLengths.LongText} characters.", "outcome");
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
