using System.Text.Json.Serialization;
using SCI_PH.Application.Common.Serialization;
using SCI_PH.Domain.Enums.Medications;

namespace SCI_PH.Application.DTOs.Clinical
{
    // Global catalog entry (Medication has no clinic).
    public sealed record MedicationSummary(
        Guid Id,
        string? Code,
        string Name,
        string? Presentation,
        string? Concentration,
        [property: JsonConverter(typeof(TextEnumConverter))] MedicationRoute? DefaultRoute,
        [property: JsonConverter(typeof(TextEnumConverter))] MedicationStatus Status);

    // One line of Prescription.AddDetail. The medication must be active in the catalog.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PrescriptionDetailInput(
        [property: JsonRequired] Guid MedicationId,
        [property: JsonRequired] decimal Dose,
        string DoseUnit,
        [property: JsonRequired, JsonConverter(typeof(TextEnumConverter))] MedicationRoute Route,
        string Frequency,
        int? DurationDays = null,
        string? Instructions = null);

    // Prescription.Draft + AddDetail per line + Issue, in one transaction: the web never stores an empty or draft
    // prescription. The prescriber comes from the session, never from the client.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CreatePrescriptionRequest(
        [property: JsonRequired] IReadOnlyList<PrescriptionDetailInput> Details,
        string? Notes = null);

    public sealed record PrescriptionDetailSummary(
        Guid Id,
        Guid MedicationId,
        string MedicationName,
        decimal Dose,
        string DoseUnit,
        [property: JsonConverter(typeof(TextEnumConverter))] MedicationRoute Route,
        string Frequency,
        int? DurationDays,
        string? Instructions);

    public sealed record PrescriptionSummary(
        Guid Id,
        Guid EncounterId,
        [property: JsonConverter(typeof(TextEnumConverter))] PrescriptionStatus Status,
        string? Notes,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? IssuedAtUtc,
        DateTimeOffset? ValidUntilUtc,
        DateTimeOffset? CancelledAtUtc,
        Guid PrescribedByStaffId,
        IReadOnlyList<PrescriptionDetailSummary> Details);

    // MedicationAdministration.Record by the current ClinicalStaff. PrescriptionId/PrescriptionDetailId optionally link
    // the dose to an issued prescription of the same encounter; the medication must then be the prescribed one.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RecordMedicationAdministrationRequest(
        [property: JsonRequired] Guid MedicationId,
        [property: JsonRequired] decimal Dose,
        string DoseUnit,
        [property: JsonRequired, JsonConverter(typeof(TextEnumConverter))] MedicationRoute Route,
        [property: JsonRequired] DateTimeOffset AdministeredAtUtc,
        Guid? PrescriptionId = null,
        Guid? PrescriptionDetailId = null,
        string? Notes = null);

    public sealed record MedicationAdministrationSummary(
        Guid Id,
        Guid EncounterId,
        Guid MedicationId,
        string MedicationName,
        Guid? PrescriptionId,
        Guid? PrescriptionDetailId,
        decimal Dose,
        string DoseUnit,
        [property: JsonConverter(typeof(TextEnumConverter))] MedicationRoute Route,
        DateTimeOffset AdministeredAtUtc,
        Guid AdministeredByStaffId,
        [property: JsonConverter(typeof(TextEnumConverter))] MedicationAdministrationStatus Status,
        string? Notes);
}
