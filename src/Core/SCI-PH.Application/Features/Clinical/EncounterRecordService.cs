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
    // Clinical records of an encounter: triage, vital signs, clinical notes and diagnoses. The first real clinical record
    // moves the encounter from Created to InProgress through ClinicalEncounter.ChangeStatus, in the same transaction as
    // the record itself. Every record re-checks the encounter's concurrency tokens, so nothing is written after another
    // transaction closed the encounter.
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

        public async Task<IReadOnlyList<ClinicalNoteSummary>> ListNotesAsync(Guid encounterId, CancellationToken cancellationToken)
        {
            await EncounterService.GetReadableAsync(_actor, _reads, _patientAccess, encounterId, cancellationToken);
            return await _reads.ListNotesAsync(encounterId, cancellationToken);
        }

        public async Task<IReadOnlyList<DiagnosisSummary>> ListDiagnosesAsync(Guid encounterId, CancellationToken cancellationToken)
        {
            await EncounterService.GetReadableAsync(_actor, _reads, _patientAccess, encounterId, cancellationToken);
            return await _reads.ListDiagnosesAsync(encounterId, cancellationToken);
        }

        // ClinicalNote.Write by the current ClinicalStaff. Notes are immutable: corrections are new notes.
        public async Task<ClinicalNoteSummary> RecordNoteAsync(Guid encounterId, RecordClinicalNoteRequest request, CancellationToken cancellationToken)
        {
            var (actor, encounter, now) = await LoadForRecordingAsync(encounterId, Permissions.ClinicalNotes.Write, cancellationToken);
            var occurredAt = ClinicalInput.ClinicalTime(request.OccurredAtUtc, now, encounter.StartedAtUtc, "occurredAtUtc");
            var note = ClinicalInput.Apply(() => ClinicalNote.Write(encounter.Id, actor.StaffId, request.Type, request.Content, occurredAt));

            StartIfCreated(encounter, now);
            _writes.Add(note);
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Create, nameof(ClinicalNote), note.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id)));
            await _writes.SaveChangesAsync(cancellationToken);

            return note.ToSummary(authorDisplayName: null);
        }

        // Diagnosis.Record by the current ClinicalStaff, recorded now.
        public async Task<DiagnosisSummary> RecordDiagnosisAsync(Guid encounterId, RecordDiagnosisRequest request, CancellationToken cancellationToken)
        {
            var (actor, encounter, now) = await LoadForRecordingAsync(encounterId, Permissions.Diagnoses.Write, cancellationToken);
            var diagnosis = ClinicalInput.Apply(() => Diagnosis.Record(
                encounter.Id, actor.StaffId, request.Description, request.Type, now, request.IsPrimary, request.CodeSystem, request.Code));

            StartIfCreated(encounter, now);
            _writes.Add(diagnosis);
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Create, nameof(Diagnosis), diagnosis.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id)));
            await _writes.SaveChangesAsync(cancellationToken);

            return diagnosis.ToSummary();
        }

        private Task<(ClinicalActor Actor, ClinicalEncounter Encounter, DateTimeOffset Now)> LoadForRecordingAsync(
            Guid encounterId, string permission, CancellationToken cancellationToken) =>
            LoadForRecordingAsync(_actor, _writes, _patientAccess, _time, encounterId, permission, cancellationToken);

        // Shared write check of every clinical record (also MedicationService): the encounter exists, belongs to one of
        // the actor's clinics, the patient rule allows the permission, and its status accepts records.
        internal static async Task<(ClinicalActor Actor, ClinicalEncounter Encounter, DateTimeOffset Now)> LoadForRecordingAsync(
            ICurrentClinicalActor currentActor,
            IClinicalWriteStore writes,
            IPatientAccessAuthorizer patientAccess,
            TimeProvider time,
            Guid encounterId,
            string permission,
            CancellationToken cancellationToken)
        {
            var actor = await currentActor.GetAsync(cancellationToken);
            var encounter = await writes.FindEncounterAsync(encounterId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(ClinicalEncounter), encounterId);
            actor.EnsureClinic(encounter.ClinicId);
            if (encounter.PatientId is { } patientId && !await patientAccess.CanWriteAsync(patientId, permission, cancellationToken))
            {
                throw new AccessDeniedException("The current user cannot write this patient's clinical records.");
            }

            if (!RecordableStatuses.Contains(encounter.Status))
            {
                throw new ResourceConflictException($"An encounter in status {encounter.Status} does not accept clinical records.");
            }

            // The status check above can be outdated by commit time; the store re-checks it inside the save.
            writes.EnsureEncounterUnchanged(encounter);
            return (actor, encounter, time.GetUtcNow());
        }

        // Created -> InProgress only through the Domain transition; an encounter already in progress is left as is.
        internal static void StartIfCreated(ClinicalEncounter encounter, DateTimeOffset now)
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
