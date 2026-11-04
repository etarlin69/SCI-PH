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

    // What a patient may see of their own record (conservative V1, explicit allowlists); the read store applies it in
    // every query, so lists, encounter detail and the Home summary and counts always agree. Clinical notes, vital signs
    // and procedures have no route at all.
    public static class PatientSelfVisibility
    {
        public static IReadOnlyList<ClinicalResultStatus> PublishedResultStatuses { get; } =
            [ClinicalResultStatus.Final, ClinicalResultStatus.Corrected];

        // A diagnosis is published only when it is definitive AND its encounter finished normally: a presumptive one, or
        // one from an encounter still open or cancelled, never reaches the patient.
        public static IReadOnlyList<DiagnosisType> PublishedDiagnosisTypes { get; } = [DiagnosisType.Definitive];

        public static IReadOnlyList<ClinicalEncounterStatus> PublishedDiagnosisEncounterStatuses { get; } =
            [ClinicalEncounterStatus.Closed];

        // A cancelled encounter appears only as itself (minimal metadata): nothing recorded in it is published, so
        // results and prescriptions, otherwise visible (Final or Corrected; once issued, never a draft), are withheld too.
        public static ClinicalEncounterStatus WithheldRecordsEncounterStatus => ClinicalEncounterStatus.Cancelled;

        // Internal workflow states never leave the API. A state added later reads as InProgress until it is classified.
        public static PatientEncounterStatus EncounterStatus(ClinicalEncounterStatus status) => status switch
        {
            ClinicalEncounterStatus.Closed => PatientEncounterStatus.Completed,
            ClinicalEncounterStatus.Cancelled => PatientEncounterStatus.Cancelled,
            _ => PatientEncounterStatus.InProgress
        };
    }
}
