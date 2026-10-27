using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Medications;

namespace SCI_PH.Application.Features.Clinical
{
    // Medication catalog, prescriptions and medication administrations of an encounter. The Kardex is not stored: it is
    // the projection of the encounter's prescriptions (with their details) and its administrations. Writes follow the
    // same rules as the other clinical records (EncounterRecordService.LoadForRecordingAsync): clinic, patient rule,
    // recordable status, Created -> InProgress, and the encounter's concurrency tokens re-checked inside the save.
    public sealed class MedicationService
    {
        public const int MaxDetailsPerPrescription = 20;

        private readonly ICurrentClinicalActor _actor;
        private readonly IClinicalReadStore _reads;
        private readonly IClinicalWriteStore _writes;
        private readonly IPatientAccessAuthorizer _patientAccess;
        private readonly TimeProvider _time;

        public MedicationService(
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

        // Medications.Read. The catalog is global, but only active clinical staff use it (same actor rule as every
        // clinical endpoint).
        public async Task<IReadOnlyList<MedicationSummary>> ListMedicationsAsync(CancellationToken cancellationToken)
        {
            await _actor.GetAsync(cancellationToken);
            return await _reads.ListMedicationsAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<PrescriptionSummary>> ListPrescriptionsAsync(Guid encounterId, CancellationToken cancellationToken)
        {
            await EncounterService.GetReadableAsync(_actor, _reads, _patientAccess, encounterId, cancellationToken);
            return await _reads.ListPrescriptionsAsync(encounterId, cancellationToken);
        }

        public async Task<IReadOnlyList<MedicationAdministrationSummary>> ListAdministrationsAsync(Guid encounterId, CancellationToken cancellationToken)
        {
            await EncounterService.GetReadableAsync(_actor, _reads, _patientAccess, encounterId, cancellationToken);
            return await _reads.ListMedicationAdministrationsAsync(encounterId, cancellationToken);
        }

        // Prescriptions.Write by the current ClinicalStaff: Draft, one AddDetail per line and Issue, saved together.
        public async Task<PrescriptionSummary> CreatePrescriptionAsync(Guid encounterId, CreatePrescriptionRequest request, CancellationToken cancellationToken)
        {
            var details = request.Details ?? [];
            if (details.Count == 0)
            {
                throw new RequestValidationException("A prescription needs at least one medication.", "details");
            }

            if (details.Count > MaxDetailsPerPrescription)
            {
                throw new RequestValidationException(
                    $"A prescription accepts at most {MaxDetailsPerPrescription} medications.", "details");
            }

            var (actor, encounter, now) = await EncounterRecordService.LoadForRecordingAsync(
                _actor, _writes, _patientAccess, _time, encounterId, Permissions.Prescriptions.Write, cancellationToken);
            for (var i = 0; i < details.Count; i++)
            {
                ClinicalInput.Id(details[i].MedicationId, $"details[{i}].medicationId");
                ClinicalInput.Defined(details[i].Route, $"details[{i}].route");
            }

            var medications = await LoadActiveMedicationsAsync(details.Select(d => d.MedicationId).ToList(), cancellationToken);
            var prescription = ClinicalInput.Apply(() => Prescription.Draft(encounter.Id, actor.StaffId, request.Notes));
            for (var i = 0; i < details.Count; i++)
            {
                var line = details[i];
                ClinicalInput.Apply(() => prescription.AddDetail(
                    line.MedicationId, line.Dose, line.DoseUnit, line.Route, line.Frequency, line.DurationDays, line.Instructions),
                    $"details[{i}]");
            }

            ClinicalInput.Apply(() => prescription.Issue(now));

            EncounterRecordService.StartIfCreated(encounter, now);
            _writes.Add(prescription);
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Create, nameof(Prescription), prescription.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id)));
            await _writes.SaveChangesAsync(cancellationToken);

            return new PrescriptionSummary(
                prescription.Id, prescription.ClinicalEncounterId, prescription.Status, prescription.Notes, prescription.CreatedAtUtc,
                prescription.IssuedAtUtc, prescription.ValidUntilUtc, prescription.CancelledAtUtc, prescription.PrescribedByStaffId,
                prescription.Details
                    .Select(d => new PrescriptionDetailSummary(
                        d.Id, d.MedicationId, medications[d.MedicationId].Name, d.Dose, d.DoseUnit, d.Route, d.Frequency, d.DurationDays, d.Instructions))
                    .ToList());
        }

        // MedicationAdministrations.Write by the current ClinicalStaff. A linked prescription must be an issued (and still
        // valid) prescription of this encounter, and the medication must be the prescribed one.
        public async Task<MedicationAdministrationSummary> RecordAdministrationAsync(
            Guid encounterId, RecordMedicationAdministrationRequest request, CancellationToken cancellationToken)
        {
            var (actor, encounter, now) = await EncounterRecordService.LoadForRecordingAsync(
                _actor, _writes, _patientAccess, _time, encounterId, Permissions.MedicationAdministrations.Write, cancellationToken);
            ClinicalInput.Id(request.MedicationId, "medicationId");
            ClinicalInput.Defined(request.Route, "route");
            var administeredAt = ClinicalInput.ClinicalTime(request.AdministeredAtUtc, now, encounter.StartedAtUtc, "administeredAtUtc");
            if (request.PrescriptionDetailId is not null && request.PrescriptionId is null)
            {
                throw new RequestValidationException("A prescription detail requires its prescription.", "prescriptionId");
            }

            var medication = (await LoadActiveMedicationsAsync([request.MedicationId], cancellationToken))[request.MedicationId];
            if (request.PrescriptionId is { } prescriptionId)
            {
                await EnsurePrescribedAsync(encounter.Id, prescriptionId, request.PrescriptionDetailId, request.MedicationId, now, cancellationToken);
            }

            var administration = ClinicalInput.Apply(() => MedicationAdministration.Record(
                encounter.Id, request.MedicationId, request.Dose, request.DoseUnit, request.Route, administeredAt, actor.StaffId,
                request.PrescriptionId, request.PrescriptionDetailId, request.Notes));

            EncounterRecordService.StartIfCreated(encounter, now);
            _writes.Add(administration);
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Create, nameof(MedicationAdministration), administration.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id)));
            await _writes.SaveChangesAsync(cancellationToken);

            return new MedicationAdministrationSummary(
                administration.Id, administration.ClinicalEncounterId, administration.MedicationId, medication.Name, administration.PrescriptionId,
                administration.PrescriptionDetailId, administration.Dose, administration.DoseUnit, administration.Route,
                administration.AdministeredAtUtc, administration.AdministeredByStaffId, administration.Status, administration.Notes);
        }

        private Task<IReadOnlyDictionary<Guid, MedicationSummary>> LoadActiveMedicationsAsync(
            IReadOnlyCollection<Guid> medicationIds, CancellationToken cancellationToken) =>
            LoadActiveMedicationsAsync(_reads, medicationIds, cancellationToken);

        // 404 for an unknown medication, 409 for one deactivated in the catalog. Shared with the prehospital
        // administration (PrehospitalEmergencyService).
        internal static async Task<IReadOnlyDictionary<Guid, MedicationSummary>> LoadActiveMedicationsAsync(
            IClinicalReadStore reads, IReadOnlyCollection<Guid> medicationIds, CancellationToken cancellationToken)
        {
            var wanted = medicationIds.Distinct().ToList();
            var found = (await reads.FindMedicationsAsync(wanted, cancellationToken)).ToDictionary(m => m.Id);
            foreach (var id in wanted)
            {
                if (!found.TryGetValue(id, out var medication))
                {
                    throw new ResourceNotFoundException(nameof(Medication), id);
                }

                if (medication.Status != MedicationStatus.Active)
                {
                    throw new ResourceConflictException($"Medication {id} is not active in the catalog.");
                }
            }

            return found;
        }

        // A prescription of another encounter is reported as not found: its existence is not disclosed.
        private async Task EnsurePrescribedAsync(
            Guid encounterId, Guid prescriptionId, Guid? detailId, Guid medicationId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var prescription = await _reads.GetPrescriptionAsync(prescriptionId, cancellationToken);
            if (prescription is null || prescription.EncounterId != encounterId)
            {
                throw new ResourceNotFoundException(nameof(Prescription), prescriptionId);
            }

            if (prescription.Status != PrescriptionStatus.Issued)
            {
                throw new ResourceConflictException($"A prescription in status {prescription.Status} cannot be administered.");
            }

            if (prescription.ValidUntilUtc is { } validUntil && now > validUntil)
            {
                throw new ResourceConflictException("The prescription is no longer valid.");
            }

            if (detailId is { } id)
            {
                var detail = prescription.Details.SingleOrDefault(d => d.Id == id)
                    ?? throw new ResourceNotFoundException(nameof(PrescriptionDetail), id);
                if (detail.MedicationId != medicationId)
                {
                    throw new RequestValidationException("medicationId must be the medication of the prescription detail.", "medicationId");
                }
            }
            else if (prescription.Details.All(d => d.MedicationId != medicationId))
            {
                throw new RequestValidationException("medicationId is not part of the prescription.", "medicationId");
            }
        }
    }
}
