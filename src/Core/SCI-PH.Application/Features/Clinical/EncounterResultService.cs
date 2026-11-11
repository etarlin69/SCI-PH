using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;

namespace SCI_PH.Application.Features.Clinical
{
    // Clinical results of an encounter (laboratory, imaging, point of care, ...) with the Domain lifecycle of ClinicalResult:
    // Request (Pending) -> RecordFinal (Final) -> Correct (Corrected, again as needed), or Cancel while Pending. Reading
    // follows the encounter's read rule (Encounters.Read); writing needs Results.Write (the controller's policy) and the
    // shared write check of every clinical record (clinic, patient rule, a status that accepts records), and the first
    // record starts care like any other. Every change is audited in the same transaction, and a state change is written only
    // if the result is still in the status it was read in: two people completing the same order never overwrite each other.
    //
    // Only Final and Corrected results are published to the patient (PatientSelfService), as before.
    public sealed class EncounterResultService
    {
        private readonly ICurrentClinicalActor _actor;
        private readonly IClinicalReadStore _reads;
        private readonly IClinicalWriteStore _writes;
        private readonly IClinicalResultStore _results;
        private readonly IPatientAccessAuthorizer _patientAccess;
        private readonly TimeProvider _time;

        public EncounterResultService(
            ICurrentClinicalActor actor,
            IClinicalReadStore reads,
            IClinicalWriteStore writes,
            IClinicalResultStore results,
            IPatientAccessAuthorizer patientAccess,
            TimeProvider time)
        {
            _actor = actor;
            _reads = reads;
            _writes = writes;
            _results = results;
            _patientAccess = patientAccess;
            _time = time;
        }

        public async Task<IReadOnlyList<ClinicalResultSummary>> ListAsync(Guid encounterId, CancellationToken cancellationToken)
        {
            await EncounterService.GetReadableAsync(_actor, _reads, _patientAccess, encounterId, cancellationToken);
            return await _results.ListForEncounterAsync(encounterId, cancellationToken);
        }

        public async Task<ClinicalResultSummary> RequestAsync(Guid encounterId, RequestClinicalResultRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            var (actor, encounter, now) = await LoadAsync(encounterId, cancellationToken);
            var result = ClinicalInput.Apply(() => ClinicalResult.Request(
                encounter.Id, request.Type, request.Name, codeSystem: request.CodeSystem, code: request.Code, referenceRange: request.ReferenceRange));

            EncounterRecordService.StartCareIfPending(encounter, now);
            _results.Add(result);
            Audit(actor, AuditActionType.Create, result, encounter, now);
            await _writes.SaveChangesAsync(cancellationToken);
            return await SummaryAsync(encounter.Id, result.Id, cancellationToken);
        }

        public Task<ClinicalResultSummary> RecordFinalAsync(
            Guid encounterId, Guid resultId, RecordClinicalResultRequest request, CancellationToken cancellationToken) =>
            TransitionAsync(encounterId, resultId, request, [ClinicalResultStatus.Pending], (result, at, staffId) =>
                result.RecordFinal(at, request.ResultText, request.NumericValue, request.Unit, staffId), cancellationToken);

        public Task<ClinicalResultSummary> CorrectAsync(
            Guid encounterId, Guid resultId, RecordClinicalResultRequest request, CancellationToken cancellationToken) =>
            TransitionAsync(encounterId, resultId, request, [ClinicalResultStatus.Final, ClinicalResultStatus.Corrected], (result, at, staffId) =>
                result.Correct(at, request.ResultText, request.NumericValue, request.Unit, staffId), cancellationToken);

        public Task<ClinicalResultSummary> CancelAsync(Guid encounterId, Guid resultId, CancellationToken cancellationToken) =>
            TransitionAsync(encounterId, resultId, request: null, [ClinicalResultStatus.Pending], (result, _, _) => result.Cancel(), cancellationToken);

        private async Task<ClinicalResultSummary> TransitionAsync(
            Guid encounterId,
            Guid resultId,
            RecordClinicalResultRequest? request,
            IReadOnlyCollection<ClinicalResultStatus> from,
            Action<ClinicalResult, DateTimeOffset, Guid> change,
            CancellationToken cancellationToken)
        {
            var (actor, encounter, now) = await LoadAsync(encounterId, cancellationToken);
            var result = await _results.FindForEncounterAsync(encounter.Id, resultId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(ClinicalResult), resultId);
            // A state that does not allow the change is a conflict (409); a missing or invalid value is a 400.
            if (!from.Contains(result.Status))
            {
                throw new ResourceConflictException($"A {result.Status} result cannot be changed this way.");
            }

            var resultedAt = request is null
                ? now
                : ClinicalInput.ClinicalTime(request.ResultedAtUtc ?? now, now, encounter.StartedAtUtc, "resultedAtUtc");
            var readStatus = result.Status;
            // Missing or invalid values are Domain rules.
            ClinicalInput.Apply(() => change(result, resultedAt, actor.StaffId));

            EncounterRecordService.StartCareIfPending(encounter, now);
            Audit(actor, AuditActionType.Update, result, encounter, now);
            await _results.SaveTransitionAsync(result, readStatus, cancellationToken);
            return await SummaryAsync(encounter.Id, result.Id, cancellationToken);
        }

        private Task<(ClinicalActor Actor, ClinicalEncounter Encounter, DateTimeOffset Now)> LoadAsync(
            Guid encounterId, CancellationToken cancellationToken) =>
            EncounterRecordService.LoadForRecordingAsync(
                _actor, _writes, _patientAccess, _time, encounterId, Permissions.Results.Write, cancellationToken);

        private void Audit(ClinicalActor actor, AuditActionType action, ClinicalResult result, ClinicalEncounter encounter, DateTimeOffset now) =>
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, action, nameof(ClinicalResult), result.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id)));

        private async Task<ClinicalResultSummary> SummaryAsync(Guid encounterId, Guid resultId, CancellationToken cancellationToken) =>
            (await _results.ListForEncounterAsync(encounterId, cancellationToken)).Single(r => r.Id == resultId);
    }
}
