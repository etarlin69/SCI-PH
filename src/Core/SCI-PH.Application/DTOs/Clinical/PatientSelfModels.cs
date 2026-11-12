using System.Text.Json.Serialization;
using SCI_PH.Application.Common.Serialization;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.DTOs.Clinical
{
    // Patient self-service (ADR-015): the authenticated patient's own record, read-only. No patient id, account id,
    // staff id, clinic id, blob path or audit data ever appears in these contracts.

    // Own demographics. The document is masked like everywhere else; email and phone are the primary active ones.
    public sealed record PatientSelfProfile(
        string GivenNames,
        string FamilyNames,
        DateOnly? DateOfBirth,
        [property: JsonConverter(typeof(TextEnumConverter))] BiologicalSex Sex,
        PatientIdentifierSummary? Document,
        string? Email,
        string? Phone);

    // The patient-facing classification of an encounter: the internal workflow state never leaves the API.
    public enum PatientEncounterStatus
    {
        InProgress = 1,
        Completed = 2,
        Cancelled = 3
    }

    // Minimal metadata; the staff's free-text reason is not published.
    public sealed record PatientSelfEncounter(
        Guid Id,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicalEncounterType Type,
        [property: JsonConverter(typeof(TextEnumConverter))] PatientEncounterStatus Status,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc,
        string? ClinicName);

    // One own encounter with what was recorded in it (same visibility rules as the lists).
    public sealed record PatientSelfEncounterDetail(
        PatientSelfEncounter Encounter,
        IReadOnlyList<PatientSelfDiagnosis> Diagnoses,
        IReadOnlyList<PatientSelfResult> Results,
        IReadOnlyList<PatientSelfPrescription> Prescriptions);

    // Allergies of the latest medical history version.
    public sealed record PatientSelfAllergy(
        Guid Id,
        string Substance,
        [property: JsonConverter(typeof(TextEnumConverter))] AllergySeverity Severity,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicalRecordStatus Status,
        string? Reaction,
        DateOnly? OnsetDate);

    // Only published diagnoses (PatientSelfVisibility): definitive, from a closed encounter.
    public sealed record PatientSelfDiagnosis(
        Guid Id,
        Guid EncounterId,
        string Description,
        [property: JsonConverter(typeof(TextEnumConverter))] DiagnosisType Type,
        DateTimeOffset RecordedAtUtc);

    // Only published results (Final or Corrected): a pending order has no value to show and a cancelled one is noise.
    public sealed record PatientSelfResult(
        Guid Id,
        Guid EncounterId,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicalResultType Type,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicalResultStatus Status,
        string Name,
        string? ResultText,
        decimal? NumericValue,
        string? Unit,
        string? ReferenceRange,
        DateTimeOffset? ResultedAtUtc);

    // Prescriptions that were issued (Issued, or Cancelled after issue); a draft is not an order and is never shown. The
    // prescriber's notes are internal and are not published.
    public sealed record PatientSelfPrescription(
        Guid Id,
        Guid EncounterId,
        [property: JsonConverter(typeof(TextEnumConverter))] PrescriptionStatus Status,
        DateTimeOffset? IssuedAtUtc,
        DateTimeOffset? ValidUntilUtc,
        DateTimeOffset? CancelledAtUtc,
        IReadOnlyList<PatientSelfPrescriptionItem> Items);

    public sealed record PatientSelfPrescriptionItem(
        string MedicationName,
        string? Presentation,
        string? Concentration,
        decimal Dose,
        string DoseUnit,
        [property: JsonConverter(typeof(TextEnumConverter))] MedicationRoute Route,
        string Frequency,
        int? DurationDays,
        string? Instructions);

    // Home screen: counts and the latest items. Read-only, no AI and no interpretation.
    public sealed record PatientSelfSummary(
        string DisplayName,
        int EncounterCount,
        int ResultCount,
        PatientSelfEncounter? LatestEncounter,
        PatientSelfResult? LatestResult,
        IReadOnlyList<PatientSelfAllergy> ActiveAllergies);

    // Read-store row of the profile: the document keeps its normalized value until Application masks it.
    public sealed record PatientSelfProfileRow(
        string GivenNames,
        string FamilyNames,
        DateOnly? DateOfBirth,
        BiologicalSex Sex,
        PatientIdentifierRow? Document,
        string? Email,
        string? Phone);
}
