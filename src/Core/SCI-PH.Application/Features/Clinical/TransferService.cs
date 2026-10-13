using System.Text.Json;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Transport;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Transport;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Application.Features.Clinical
{
    // Transfers of an encounter between clinics (ClinicalTransfer: Requested -> InTransit -> Received).
    // - Request: from the encounter's clinic; the encounter keeps its status (the patient has not left yet).
    // - Dispatch: from the origin clinic; transfer InTransit and encounter Transferred in one transaction.
    // - Cancel: from the origin clinic; a Requested transfer only, an InTransit one also returns the encounter to InProgress.
    // - Receive: from the destination clinic stored in the transfer; transfer Received and the encounter moves to the
    //   destination (ClinicalEncounter.ReceiveAtClinic) in one transaction. The origin clinic keeps the trace
    //   (transfer and audit) but loses normal clinical access.
    // Complete is not exposed: the Domain does not define when a received transfer is completed.
    // Dispatch, Cancel and Receive also change the transfer Status (a concurrency token), and Dispatch and Receive the
    // encounter Status (another one), so concurrent dispatches, cancellations, receptions or a
    // concurrent close lose with a 409. The actor always comes from the session.
    public sealed class TransferService
    {
        public const int MaxListSize = 100;

        public static IReadOnlyCollection<ClinicalTransferStatus> IncomingStatuses { get; } =
            [ClinicalTransferStatus.Requested, ClinicalTransferStatus.InTransit];

        private readonly ICurrentClinicalActor _actor;
        private readonly IClinicalReadStore _reads;
        private readonly IClinicalWriteStore _writes;
        private readonly IPatientAccessAuthorizer _patientAccess;
        private readonly TimeProvider _time;

        public TransferService(
            ICurrentClinicalActor actor,
            IClinicalReadStore reads,
            IClinicalWriteStore writes,
            IPatientAccessAuthorizer patientAccess,
            TimeProvider time)
        {
            _actor = actor;
            _reads = reads;
            _writes = writes;
            _patientAccess = patientAccess;
            _time = time;
        }

        // Transfers.Read: transfers on their way to the actor's clinics (or to the requested one, validated).
        public async Task<IReadOnlyList<ClinicalTransferSummary>> ListIncomingAsync(Guid? clinicId, CancellationToken cancellationToken)
        {
            var actor = await _actor.GetAsync(cancellationToken);
            IReadOnlyCollection<Guid> clinics = clinicId is { } requested ? [actor.ResolveClinic(requested)] : actor.ActiveClinicIds;
            var rows = await _reads.ListTransfersToAsync(clinics, IncomingStatuses, MaxListSize, cancellationToken);
            return rows.Select(ToSummary).ToList();
        }

        // Transfers.Read: visible to the origin and the destination clinic.
        public async Task<ClinicalTransferSummary> GetAsync(Guid transferId, CancellationToken cancellationToken)
        {
            var actor = await _actor.GetAsync(cancellationToken);
            var row = await _reads.GetTransferAsync(transferId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(ClinicalTransfer), transferId);
            if (!actor.ActiveClinicIds.Contains(row.DestinationClinic.Id)
                && (row.OriginClinic is null || !actor.ActiveClinicIds.Contains(row.OriginClinic.Id)))
            {
                throw new AccessDeniedException("The transfer does not involve a clinic the user is affiliated with.");
            }

            return ToSummary(row);
        }

        // Transfers.Manage. Only one active (Requested or InTransit) transfer per encounter.
        public async Task<ClinicalTransferSummary> RequestAsync(Guid encounterId, RequestTransferRequest request, CancellationToken cancellationToken)
        {
            ClinicalInput.Id(request.DestinationClinicId, "destinationClinicId");
            var actor = await _actor.GetAsync(cancellationToken);
            var encounter = await _writes.FindEncounterAsync(encounterId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(ClinicalEncounter), encounterId);
            actor.EnsureClinic(encounter.ClinicId);
            await EnsureCanManageAsync(encounter, cancellationToken);
            if (!encounter.CanChangeStatusTo(ClinicalEncounterStatus.Transferred))
            {
                throw new ResourceConflictException($"An encounter in status {encounter.Status} cannot be transferred.");
            }

            var destinationStatus = await _reads.GetClinicStatusAsync(request.DestinationClinicId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(Clinic), request.DestinationClinicId);
            if (destinationStatus != ClinicStatus.Active)
            {
                throw new ResourceConflictException("The destination clinic is not active.");
            }

            if (await _reads.HasActiveTransferAsync(encounter.Id, cancellationToken))
            {
                throw new ResourceConflictException("The encounter already has an active transfer.");
            }

            var now = _time.GetUtcNow();
            // Same origin and destination is a Domain rule (InvalidClinicalTransferException -> 400).
            var transfer = ClinicalInput.Apply(() => ClinicalTransfer.Request(
                encounter.Id, request.DestinationClinicId, now, encounter.ClinicId, reason: request.Reason, notes: request.Notes));

            _writes.EnsureEncounterUnchanged(encounter);
            _writes.Add(transfer);
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Create, nameof(ClinicalTransfer), transfer.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id)));
            await _writes.SaveChangesAsync(cancellationToken);

            return await GetSavedAsync(transfer.Id, cancellationToken);
        }

        // Transfers.Manage from the origin clinic: the patient leaves (transfer InTransit, encounter Transferred).
        public async Task<ClinicalTransferSummary> DispatchAsync(Guid transferId, CancellationToken cancellationToken)
        {
            var (actor, transfer, encounter) = await LoadAsync(transferId, cancellationToken);
            actor.EnsureClinic(transfer.OriginClinicId);
            actor.EnsureClinic(encounter.ClinicId);
            await EnsureCanManageAsync(encounter, cancellationToken);
            if (!encounter.CanChangeStatusTo(ClinicalEncounterStatus.Transferred))
            {
                throw new ResourceConflictException($"An encounter in status {encounter.Status} cannot be transferred.");
            }

            var now = _time.GetUtcNow();
            var previousStatus = encounter.Status;
            Conflict(() => transfer.Dispatch(actor.StaffId, now));
            Conflict(() => encounter.ChangeStatus(ClinicalEncounterStatus.Transferred, now));

            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Transfer, nameof(ClinicalTransfer), transfer.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id, ChangesJson: JsonSerializer.Serialize(new
                {
                    transfer = new[] { ClinicalTransferStatus.Requested.ToString(), transfer.Status.ToString() },
                    encounterStatus = new[] { previousStatus.ToString(), encounter.Status.ToString() }
                }))));
            await _writes.SaveChangesAsync(cancellationToken);

            return await GetSavedAsync(transfer.Id, cancellationToken);
        }

        // Transfers.Manage from the destination clinic stored in the transfer: the encounter moves to that clinic.
        public async Task<ClinicalTransferSummary> ReceiveAsync(Guid transferId, CancellationToken cancellationToken)
        {
            var (actor, transfer, encounter) = await LoadAsync(transferId, cancellationToken);
            actor.EnsureClinic(transfer.DestinationClinicId);
            await EnsureCanManageAsync(encounter, cancellationToken);

            var now = _time.GetUtcNow();
            var previousStatus = encounter.Status;
            var previousClinic = encounter.ClinicId;
            Conflict(() => transfer.Receive(actor.StaffId, now));
            Conflict(() => encounter.ReceiveAtClinic(transfer.DestinationClinicId, now));

            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Transfer, nameof(ClinicalTransfer), transfer.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id, ChangesJson: JsonSerializer.Serialize(new
                {
                    transfer = new[] { ClinicalTransferStatus.InTransit.ToString(), transfer.Status.ToString() },
                    encounterStatus = new[] { previousStatus.ToString(), encounter.Status.ToString() },
                    clinicId = new[] { previousClinic?.ToString(), encounter.ClinicId?.ToString() }
                }))));
            await _writes.SaveChangesAsync(cancellationToken);

            return await GetSavedAsync(transfer.Id, cancellationToken);
        }

        // Transfers.Manage from the origin clinic (ClinicalTransfer.Cancel; the Domain records no reason).
        // - Requested: only the transfer is cancelled; the request had not changed the encounter.
        // - InTransit: the transfer is cancelled and the encounter returns Transferred -> InProgress (the Domain transition
        //   for a cancelled transfer), still at the origin clinic.
        // Received, Completed or already cancelled transfers cannot be cancelled (409).
        public async Task<ClinicalTransferSummary> CancelAsync(Guid transferId, CancellationToken cancellationToken)
        {
            var (actor, transfer, encounter) = await LoadAsync(transferId, cancellationToken);
            actor.EnsureClinic(transfer.OriginClinicId);
            await EnsureCanManageAsync(encounter, cancellationToken);

            var now = _time.GetUtcNow();
            var previousTransfer = transfer.Status;
            var previousEncounter = encounter.Status;
            Conflict(() => transfer.Cancel(now));
            if (previousTransfer == ClinicalTransferStatus.InTransit)
            {
                actor.EnsureClinic(encounter.ClinicId);
                Conflict(() => encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, now));
            }

            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Transfer, nameof(ClinicalTransfer), transfer.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id, ChangesJson: JsonSerializer.Serialize(new
                {
                    transfer = new[] { previousTransfer.ToString(), transfer.Status.ToString() },
                    encounterStatus = new[] { previousEncounter.ToString(), encounter.Status.ToString() }
                }))));
            await _writes.SaveChangesAsync(cancellationToken);

            return await GetSavedAsync(transfer.Id, cancellationToken);
        }

        private async Task<(ClinicalActor Actor, ClinicalTransfer Transfer, ClinicalEncounter Encounter)> LoadAsync(
            Guid transferId, CancellationToken cancellationToken)
        {
            var actor = await _actor.GetAsync(cancellationToken);
            var transfer = await _writes.FindTransferAsync(transferId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(ClinicalTransfer), transferId);
            var encounter = await _writes.FindEncounterAsync(transfer.ClinicalEncounterId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(ClinicalEncounter), transfer.ClinicalEncounterId);
            return (actor, transfer, encounter);
        }

        private async Task EnsureCanManageAsync(ClinicalEncounter encounter, CancellationToken cancellationToken)
        {
            if (encounter.PatientId is { } patientId
                && !await _patientAccess.CanWriteAsync(patientId, Permissions.Transfers.Manage, cancellationToken))
            {
                throw new AccessDeniedException("The current user cannot manage this patient's transfers.");
            }
        }

        private async Task<ClinicalTransferSummary> GetSavedAsync(Guid transferId, CancellationToken cancellationToken) =>
            ToSummary(await _reads.GetTransferAsync(transferId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(ClinicalTransfer), transferId));

        // Transfer and encounter state rules hit by a valid request are conflicts with the current state (409).
        private static void Conflict(Action domainCall)
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

        private static ClinicalTransferSummary ToSummary(TransferRow row) =>
            new(row.Id, row.EncounterId, row.Status, row.OriginClinic, row.DestinationClinic, row.Ambulance, row.RequestedAtUtc,
                row.SentAtUtc, row.SentByStaffId, row.ReceivedAtUtc, row.ReceivedByStaffId, row.CancelledAtUtc,
                row.Patient?.ToSummary(), row.TemporaryPatientCode, row.Reason, row.Notes, row.LatestTriagePriority, row.EncounterStatus);
    }
}
