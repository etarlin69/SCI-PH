using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Enums.Encounters;
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

        Task<IReadOnlyList<PatientRow>> FindPatientsByNameAsync(string term, int limit, CancellationToken cancellationToken);

        Task<EncounterRow?> GetEncounterAsync(Guid encounterId, CancellationToken cancellationToken);

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
    }
}
