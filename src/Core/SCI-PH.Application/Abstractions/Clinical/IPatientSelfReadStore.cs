using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Enums.Encounters;

namespace SCI_PH.Application.Abstractions.Clinical
{
    // Reads of ONE patient's own record (ADR-015). Every query is filtered by the patientId that PatientSelfService
    // resolved from the authenticated account; an optional encounterId only narrows that set, it never widens it, so
    // another patient's encounter simply yields nothing.
    public interface IPatientSelfReadStore
    {
        Task<PatientSelfProfileRow?> GetProfileAsync(Guid patientId, CancellationToken cancellationToken);

        Task<int> CountEncountersAsync(Guid patientId, CancellationToken cancellationToken);

        // Most recent first.
        Task<IReadOnlyList<PatientSelfEncounter>> ListEncountersAsync(Guid patientId, int limit, CancellationToken cancellationToken);

        // Null when the encounter does not exist or belongs to someone else.
        Task<PatientSelfEncounter?> GetEncounterAsync(Guid patientId, Guid encounterId, CancellationToken cancellationToken);

        Task<IReadOnlyList<PatientSelfAllergy>> ListAllergiesAsync(Guid patientId, CancellationToken cancellationToken);

        Task<IReadOnlyList<PatientSelfDiagnosis>> ListDiagnosesAsync(
            Guid patientId, Guid? encounterId, int limit, CancellationToken cancellationToken);

        Task<IReadOnlyList<PatientSelfPrescription>> ListPrescriptionsAsync(
            Guid patientId, Guid? encounterId, int limit, CancellationToken cancellationToken);

        Task<int> CountResultsAsync(Guid patientId, CancellationToken cancellationToken);

        Task<IReadOnlyList<PatientSelfResult>> ListResultsAsync(
            Guid patientId, Guid? encounterId, int limit, CancellationToken cancellationToken);
    }

    // What a patient may see of their own record; the read store applies it in every query. Prescriptions are visible
    // once issued (still Issued, or Cancelled after issue): a draft, even a cancelled one, was never an order.
    public static class PatientSelfVisibility
    {
        public static IReadOnlyList<ClinicalResultStatus> PublishedResultStatuses { get; } =
            [ClinicalResultStatus.Final, ClinicalResultStatus.Corrected];
    }
}
