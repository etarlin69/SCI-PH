using System.Text.Json;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Application.Features.Clinical
{
    // Encounter lifecycle at the clinic: open (identified or NN), read, list the open ones and identify an NN patient.
    // Every operation is scoped to the clinics of the current ClinicalStaff.
    public sealed class EncounterService
    {
        public const int MaxListSize = 200;

        // Not shown as open work of the clinic: finished, cancelled or handed over to another facility.
        public static IReadOnlyCollection<ClinicalEncounterStatus> NotOpenStatuses { get; } =
            [ClinicalEncounterStatus.Closed, ClinicalEncounterStatus.Cancelled, ClinicalEncounterStatus.Transferred];

        private readonly ICurrentClinicalActor _actor;
        private readonly IClinicalReadStore _reads;
        private readonly IClinicalWriteStore _writes;
        private readonly IClinicalStaffDirectory _staff;
        private readonly IPatientAccessAuthorizer _patientAccess;
        private readonly TimeProvider _time;

        public EncounterService(
            ICurrentClinicalActor actor,
            IClinicalReadStore reads,
            IClinicalWriteStore writes,
            IClinicalStaffDirectory staff,
            IPatientAccessAuthorizer patientAccess,
            TimeProvider time)
        {
            _actor = actor;
            _reads = reads;
            _writes = writes;
            _staff = staff;
            _patientAccess = patientAccess;
            _time = time;
        }

        public async Task<ClinicalContextSummary> GetContextAsync(CancellationToken cancellationToken)
        {
            var actor = await _actor.GetAsync(cancellationToken);
            return new ClinicalContextSummary(actor.StaffId, await _staff.GetClinicsAsync(actor.ActiveClinicIds, cancellationToken));
        }

        // CreateForPatient when the patient is known, CreateUnidentified (NN) otherwise. Starts now, at the resolved clinic.
        public async Task<EncounterSummary> CreateAsync(CreateEncounterRequest request, CancellationToken cancellationToken)
        {
            var type = ClinicalInput.Defined(request.Type, "type");
            var actor = await _actor.GetAsync(cancellationToken);
            var clinicId = actor.ResolveClinic(request.ClinicId);
            var now = _time.GetUtcNow();

            ClinicalEncounter encounter;
            if (request.PatientId is { } requestedPatientId)
            {
                var patientId = ClinicalInput.Id(requestedPatientId, "patientId");
                if (!await _reads.PatientExistsAsync(patientId, cancellationToken))
                {
                    throw new ResourceNotFoundException(nameof(Patient), patientId);
                }

                encounter = ClinicalInput.Apply(() => ClinicalEncounter.CreateForPatient(patientId, type, now, clinicId, reason: request.Reason));
            }
            else
            {
                if (!ClinicalEncounter.TypesAllowingUnidentifiedPatient.Contains(type))
                {
                    throw new RequestValidationException($"A {type} encounter requires an identified patient.", "patientId");
                }

                encounter = ClinicalInput.Apply(() => ClinicalEncounter.CreateUnidentified(type, now, clinicId, reason: request.Reason));
            }

            _writes.Add(encounter);
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Create, nameof(ClinicalEncounter), encounter.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id)));
            await _writes.SaveChangesAsync(cancellationToken);

            return (await _reads.GetEncounterAsync(encounter.Id, cancellationToken))!.ToSummary();
        }

        public async Task<IReadOnlyList<EncounterSummary>> ListOpenAsync(Guid? clinicId, CancellationToken cancellationToken)
        {
            var actor = await _actor.GetAsync(cancellationToken);
            var rows = await _reads.ListEncountersAsync(
                actor.ResolveClinic(clinicId), NotOpenStatuses, onlyUnidentified: false, MaxListSize, cancellationToken);
            return rows.Select(row => row.ToSummary()).ToList();
        }

        public async Task<EncounterSummary> GetAsync(Guid encounterId, CancellationToken cancellationToken) =>
            (await GetReadableAsync(_actor, _reads, _patientAccess, encounterId, cancellationToken)).ToSummary();

        // Links the NN encounter to the real patient. The temporary code is kept as history and the change is
        // audited in the same transaction. A concurrent identification loses on the PatientId concurrency token (409).
        public async Task IdentifyPatientAsync(Guid encounterId, IdentifyEncounterPatientRequest request, CancellationToken cancellationToken)
        {
            var patientId = ClinicalInput.Id(request.PatientId, "patientId");
            var actor = await _actor.GetAsync(cancellationToken);
            var encounter = await _writes.FindEncounterAsync(encounterId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(ClinicalEncounter), encounterId);
            actor.EnsureClinic(encounter.ClinicId);
            if (!await _reads.PatientExistsAsync(patientId, cancellationToken))
            {
                throw new ResourceNotFoundException(nameof(Patient), patientId);
            }

            var now = _time.GetUtcNow();
            try
            {
                ClinicalInput.Apply(() => encounter.IdentifyPatient(patientId, actor.StaffId, now));
            }
            catch (Exception exception) when (exception is PatientAlreadyIdentifiedException or InvalidClinicalEncounterStateException)
            {
                throw new ResourceConflictException(exception.Message, exception);
            }

            var changes = JsonSerializer.Serialize(new
            {
                temporaryPatientCode = encounter.TemporaryPatientCode?.Value,
                patientId
            });
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Identify, nameof(ClinicalEncounter), encounter.Id.ToString(), now,
                new AuditContext(patientId, encounter.Id, ChangesJson: changes)));
            await _writes.SaveChangesAsync(cancellationToken);
        }

        // Shared read check: the encounter exists, belongs to one of the actor's clinics and, when it has a patient,
        // the resource-based patient rule (IPatientAccessAuthorizer) allows reading it.
        internal static async Task<EncounterRow> GetReadableAsync(
            ICurrentClinicalActor currentActor,
            IClinicalReadStore reads,
            IPatientAccessAuthorizer patientAccess,
            Guid encounterId,
            CancellationToken cancellationToken)
        {
            var actor = await currentActor.GetAsync(cancellationToken);
            var encounter = await reads.GetEncounterAsync(encounterId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(ClinicalEncounter), encounterId);
            actor.EnsureClinic(encounter.ClinicId);
            if (encounter.PatientId is { } patientId
                && !await patientAccess.CanReadAsync(patientId, Permissions.Encounters.Read, cancellationToken))
            {
                throw new AccessDeniedException("The current user cannot read this patient's encounters.");
            }

            return encounter;
        }
    }
}
