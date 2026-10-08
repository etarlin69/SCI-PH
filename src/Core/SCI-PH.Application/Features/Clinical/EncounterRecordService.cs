using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Application.Features.Clinical
{
    // Triage and vital signs of an encounter. The first real clinical record moves the encounter from Created to
    // InProgress through ClinicalEncounter.ChangeStatus, in the same transaction as the record itself.
    public sealed class EncounterRecordService
    {
        public const int MaxMeasurementsPerTriage = 20;

        // Statuses that accept clinical records. Created is started by the record; Transferred, Closed and
        // Cancelled are conflicts.
        public static IReadOnlyCollection<ClinicalEncounterStatus> RecordableStatuses { get; } =
        [
            ClinicalEncounterStatus.Created,
            ClinicalEncounterStatus.InProgress,
            ClinicalEncounterStatus.Received,
            ClinicalEncounterStatus.InEvolution
        ];

        private readonly ICurrentClinicalActor _actor;
        private readonly ICurrentUser _currentUser;
        private readonly IClinicalReadStore _reads;
        private readonly IClinicalWriteStore _writes;
        private readonly IPatientAccessAuthorizer _patientAccess;
        private readonly TimeProvider _time;

        public EncounterRecordService(
            ICurrentClinicalActor actor,
            ICurrentUser currentUser,
            IClinicalReadStore reads,
            IClinicalWriteStore writes,
            IPatientAccessAuthorizer patientAccess,
            TimeProvider time)
        {
            _actor = actor;
            _currentUser = currentUser;
            _reads = reads;
            _writes = writes;
            _patientAccess = patientAccess;
            _time = time;
        }

        public async Task<IReadOnlyList<TriageSummary>> ListTriagesAsync(Guid encounterId, CancellationToken cancellationToken)
        {
            await EncounterService.GetReadableAsync(_actor, _reads, _patientAccess, encounterId, cancellationToken);
            return await _reads.ListTriagesAsync(encounterId, cancellationToken);
        }

        public async Task<IReadOnlyList<VitalSignSummary>> ListVitalSignsAsync(Guid encounterId, CancellationToken cancellationToken)
        {
            await EncounterService.GetReadableAsync(_actor, _reads, _patientAccess, encounterId, cancellationToken);
            return await _reads.ListVitalSignsAsync(encounterId, cancellationToken);
        }

        // Triage.Record plus one VitalSign.Record per measurement, linked to the new triage. Measurements also
        // need VitalSigns.Write.
        public async Task<TriageSummary> RecordTriageAsync(Guid encounterId, RecordTriageRequest request, CancellationToken cancellationToken)
        {
            var measurements = request.Measurements ?? [];
            if (measurements.Count > 0 && !_currentUser.HasPermission(Permissions.VitalSigns.Write))
            {
                throw new AccessDeniedException($"Recording measurements requires the {Permissions.VitalSigns.Write} permission.");
            }

            if (measurements.Count > MaxMeasurementsPerTriage)
            {
                throw new RequestValidationException(
                    $"A triage accepts at most {MaxMeasurementsPerTriage} measurements.", "measurements");
            }

            var (actor, encounter, now) = await LoadForRecordingAsync(encounterId, Permissions.Triage.Write, cancellationToken);
            var performedAt = ClinicalInput.ClinicalTime(request.PerformedAtUtc ?? now, now, encounter.StartedAtUtc, "performedAtUtc");
            for (var i = 0; i < measurements.Count; i++)
            {
                ClinicalInput.ClinicalTime(measurements[i].MeasuredAtUtc, now, encounter.StartedAtUtc, $"measurements[{i}].measuredAtUtc");
            }

            var triage = ClinicalInput.Apply(() => Triage.Record(
                encounter.Id, request.Priority, actor.StaffId, performedAt, request.Reason, request.Observations));
            // Out-of-range values are a DomainException (400).
            var vitalSigns = measurements
                .Select(m => ClinicalInput.Apply(() => VitalSign.Record(
                    encounter.Id, m.Type, m.Value, m.MeasuredAtUtc, actor.StaffId, triage.Id)))
                .ToList();

            StartIfCreated(encounter, now);
            _writes.Add(triage);
            vitalSigns.ForEach(_writes.Add);
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Create, nameof(Triage), triage.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id)));
            await _writes.SaveChangesAsync(cancellationToken);

            return triage.ToSummary();
        }

        public async Task<VitalSignSummary> RecordVitalSignAsync(Guid encounterId, RecordVitalSignRequest request, CancellationToken cancellationToken)
        {
            var (actor, encounter, now) = await LoadForRecordingAsync(encounterId, Permissions.VitalSigns.Write, cancellationToken);
            var measuredAt = ClinicalInput.ClinicalTime(request.MeasuredAtUtc, now, encounter.StartedAtUtc, "measuredAtUtc");
            var vitalSign = ClinicalInput.Apply(() => VitalSign.Record(
                encounter.Id, request.Type, request.Value, measuredAt, actor.StaffId));

            StartIfCreated(encounter, now);
            _writes.Add(vitalSign);
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Create, nameof(VitalSign), vitalSign.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id)));
            await _writes.SaveChangesAsync(cancellationToken);

            return vitalSign.ToSummary();
        }

        private async Task<(ClinicalActor Actor, ClinicalEncounter Encounter, DateTimeOffset Now)> LoadForRecordingAsync(
            Guid encounterId, string permission, CancellationToken cancellationToken)
        {
            var actor = await _actor.GetAsync(cancellationToken);
            var encounter = await _writes.FindEncounterAsync(encounterId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(ClinicalEncounter), encounterId);
            actor.EnsureClinic(encounter.ClinicId);
            if (encounter.PatientId is { } patientId && !await _patientAccess.CanWriteAsync(patientId, permission, cancellationToken))
            {
                throw new AccessDeniedException("The current user cannot write this patient's clinical records.");
            }

            if (!RecordableStatuses.Contains(encounter.Status))
            {
                throw new ResourceConflictException($"An encounter in status {encounter.Status} does not accept clinical records.");
            }

            return (actor, encounter, _time.GetUtcNow());
        }

        // Created -> InProgress only through the Domain transition; an encounter already in progress is left as is.
        private static void StartIfCreated(ClinicalEncounter encounter, DateTimeOffset now)
        {
            if (encounter.Status != ClinicalEncounterStatus.Created)
            {
                return;
            }

            try
            {
                encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, now);
            }
            catch (InvalidClinicalEncounterStateException exception)
            {
                throw new ResourceConflictException(exception.Message, exception);
            }
        }
    }
}
