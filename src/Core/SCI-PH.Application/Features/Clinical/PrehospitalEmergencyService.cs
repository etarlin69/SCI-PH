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
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;

namespace SCI_PH.Application.Features.Clinical
{
    // Prehospital emergency opened by a paramedic from the mobile app: a Prehospital encounter WITHOUT a clinic (the
    // paramedic's base clinic is not where the emergency happens) and with an NN patient. The paramedic who opens it is
    // assigned to it (EncounterStaffAssignment, function Paramedic), which is what gives access to this concrete case
    // (IEncounterAccessService). On arrival, TransferService/ReceiveAtClinic sets the destination clinic on the SAME
    // encounter. Opening is idempotent per (user, operation, clientRequestId): see IIdempotencyStore.
    public sealed class PrehospitalEmergencyService
    {
        // Server-controlled operation name of the idempotency receipts; a new version gets a new name.
        public const string OpenOperation = "prehospital-emergency.open.v1";

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
        private readonly TimeProvider _time;

        public PrehospitalEmergencyService(
            ICurrentClinicalActor actor,
            IEncounterAccessService access,
            IClinicalReadStore reads,
            IClinicalWriteStore writes,
            IIdempotencyStore receipts,
            IPatientAccessAuthorizer patientAccess,
            TimeProvider time)
        {
            _actor = actor;
            _access = access;
            _reads = reads;
            _writes = writes;
            _receipts = receipts;
            _patientAccess = patientAccess;
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
                return await ReplayAsync(actor, existing, requestHash, cancellationToken);
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
                return await ReplayAsync(actor, winner, requestHash, cancellationToken);
            }

            return new OpenPrehospitalEmergencyResult(await ReadAsync(actor, encounter.Id, cancellationToken), Created: true);
        }

        public async Task<PrehospitalEmergencySummary> GetAsync(Guid emergencyId, CancellationToken cancellationToken) =>
            await ReadAsync(await _actor.GetAsync(cancellationToken), emergencyId, cancellationToken);

        private async Task<OpenPrehospitalEmergencyResult> ReplayAsync(
            ClinicalActor actor, IdempotencyReceipt receipt, string requestHash, CancellationToken cancellationToken)
        {
            if (!string.Equals(receipt.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new ResourceConflictException(
                    "clientRequestId was already used with different data. Start the emergency again with a new request.");
            }

            return new OpenPrehospitalEmergencyResult(await ReadAsync(actor, receipt.ResourceId, cancellationToken), Created: false);
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
