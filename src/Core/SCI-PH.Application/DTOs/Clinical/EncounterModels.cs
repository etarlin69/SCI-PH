using System.Text.Json.Serialization;
using SCI_PH.Application.Common.Serialization;
using SCI_PH.Domain.Enums.Encounters;

namespace SCI_PH.Application.DTOs.Clinical
{
    // PatientId null opens an NN encounter (Emergency only on the web). ClinicId is required only when the user
    // has several active affiliations, and is always validated against them.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CreateEncounterRequest(
        [property: JsonRequired, JsonConverter(typeof(TextEnumConverter))] ClinicalEncounterType Type,
        Guid? PatientId = null,
        string? Reason = null,
        Guid? ClinicId = null);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record IdentifyEncounterPatientRequest([property: JsonRequired] Guid PatientId);

    // Clinical read model (Encounters.Read).
    public sealed record EncounterSummary(
        Guid Id,
        Guid? PatientId,
        PatientSummary? Patient,
        string? TemporaryPatientCode,
        Guid? ClinicId,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicalEncounterType Type,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicalEncounterStatus Status,
        string? Reason,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc,
        DateTimeOffset? IdentifiedAtUtc,
        [property: JsonConverter(typeof(TextEnumConverter))] TriagePriority? LatestTriagePriority);

    // Administrative projection for reception and NN identification. It deliberately carries no clinical content:
    // no reason, triage, vital signs, notes, diagnoses, results, prescriptions or administrations.
    public sealed record AdmissionEncounterSummary(
        Guid EncounterId,
        Guid? PatientId,
        string? PatientDisplayName,
        string? TemporaryPatientCode,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicalEncounterType Type,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicalEncounterStatus Status,
        DateTimeOffset StartedAtUtc,
        Guid? ClinicId);

    // Read-store row shared by the clinical and admission projections.
    public sealed record EncounterRow(
        Guid Id,
        Guid? PatientId,
        PatientRow? Patient,
        string? TemporaryPatientCode,
        Guid? ClinicId,
        ClinicalEncounterType Type,
        ClinicalEncounterStatus Status,
        string? Reason,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc,
        DateTimeOffset? IdentifiedAtUtc,
        TriagePriority? LatestTriagePriority);

    public sealed record ClinicSummary(Guid Id, string Name);

    // Clinics the current user may act in, so a client with several affiliations can choose one.
    public sealed record ClinicalContextSummary(Guid StaffId, IReadOnlyList<ClinicSummary> Clinics);
}
