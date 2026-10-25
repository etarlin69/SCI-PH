using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Transport;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Abstractions.Clinical
{
    // No-tracking queries over the clinical schema. Authorization and masking are applied by the use cases.
    public interface IClinicalReadStore
    {
        Task<bool> PatientExistsAsync(Guid patientId, CancellationToken cancellationToken);

        // Active identifier with the same type, issuer and normalized value (uniqueness of identity-bearing types).
        Task<bool> ActiveIdentifierExistsAsync(
            PatientIdentifierType type, string issuer, string normalizedValue, CancellationToken cancellationToken);

        Task<PatientRow?> GetPatientAsync(Guid patientId, CancellationToken cancellationToken);

        // Exact match on PatientIdentifier.NormalizedValue among active identifiers.
        Task<IReadOnlyList<PatientRow>> FindPatientsByIdentifierAsync(
            PatientIdentifierType type, string normalizedValue, int limit, CancellationToken cancellationToken);

        // The patient holding this active identifier in the SCI-PH platform scope (empty issuer). For identity-bearing
        // types the unique index on (type, issuer, normalized value) guarantees 0 or 1; an identifier registered with
        // another issuer is never matched.
        Task<Guid?> FindPatientIdByPlatformIdentifierAsync(
            PatientIdentifierType type, string normalizedValue, CancellationToken cancellationToken);

        Task<IReadOnlyList<PatientRow>> FindPatientsByNameAsync(string term, int limit, CancellationToken cancellationToken);

        Task<EncounterRow?> GetEncounterAsync(Guid encounterId, CancellationToken cancellationToken);

        // The staff member has an open (not unassigned) EncounterStaffAssignment on the encounter.
        Task<bool> HasActiveStaffAssignmentAsync(Guid encounterId, Guid staffId, CancellationToken cancellationToken);

        Task<IReadOnlyList<EncounterRow>> ListEncountersAsync(
            Guid clinicId,
            IReadOnlyCollection<ClinicalEncounterStatus> excludedStatuses,
            bool onlyUnidentified,
            int limit,
            CancellationToken cancellationToken);

        // Newest first.
        Task<IReadOnlyList<TriageSummary>> ListTriagesAsync(Guid encounterId, CancellationToken cancellationToken);

        // Newest first.
        Task<IReadOnlyList<VitalSignSummary>> ListVitalSignsAsync(Guid encounterId, CancellationToken cancellationToken);

        // Newest first (by clinical time), with the author's name.
        Task<IReadOnlyList<ClinicalNoteSummary>> ListNotesAsync(Guid encounterId, CancellationToken cancellationToken);

        // Primary first, then newest.
        Task<IReadOnlyList<DiagnosisSummary>> ListDiagnosesAsync(Guid encounterId, CancellationToken cancellationToken);

        // Whole catalog (active and inactive), by name.
        Task<IReadOnlyList<MedicationSummary>> ListMedicationsAsync(CancellationToken cancellationToken);

        // Only the ids that exist.
        Task<IReadOnlyList<MedicationSummary>> FindMedicationsAsync(IReadOnlyCollection<Guid> medicationIds, CancellationToken cancellationToken);

        // Newest first, each with its lines.
        Task<IReadOnlyList<PrescriptionSummary>> ListPrescriptionsAsync(Guid encounterId, CancellationToken cancellationToken);

        Task<PrescriptionSummary?> GetPrescriptionAsync(Guid prescriptionId, CancellationToken cancellationToken);

        // Newest first (by administration time).
        Task<IReadOnlyList<MedicationAdministrationSummary>> ListMedicationAdministrationsAsync(Guid encounterId, CancellationToken cancellationToken);

        // Null when the clinic does not exist.
        Task<ClinicStatus?> GetClinicStatusAsync(Guid clinicId, CancellationToken cancellationToken);

        // Transfers of the encounter that are still Requested or InTransit.
        Task<bool> HasActiveTransferAsync(Guid encounterId, CancellationToken cancellationToken);

        Task<TransferRow?> GetTransferAsync(Guid transferId, CancellationToken cancellationToken);

        // Transfers in the given statuses whose destination is one of the clinics, most recently requested first.
        Task<IReadOnlyList<TransferRow>> ListTransfersToAsync(
            IReadOnlyCollection<Guid> destinationClinicIds,
            IReadOnlyCollection<ClinicalTransferStatus> statuses,
            int limit,
            CancellationToken cancellationToken);
    }
}
