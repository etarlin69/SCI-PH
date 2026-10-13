using System.Text.Json.Serialization;
using SCI_PH.Application.Common.Serialization;
using SCI_PH.Domain.Enums.Encounters;

namespace SCI_PH.Application.DTOs.Clinical
{
    // ClinicalNote.Write: the author comes from the session, never from the client.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RecordClinicalNoteRequest(
        [property: JsonRequired, JsonConverter(typeof(TextEnumConverter))] ClinicalNoteType Type,
        string Content,
        [property: JsonRequired] DateTimeOffset OccurredAtUtc);

    // Diagnosis.Record: recorded now by the current ClinicalStaff. A code requires its code system (Domain rule).
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RecordDiagnosisRequest(
        string Description,
        [property: JsonRequired, JsonConverter(typeof(TextEnumConverter))] DiagnosisType Type,
        bool IsPrimary = false,
        string? CodeSystem = null,
        string? Code = null);

    // Closing ends the encounter (ClinicalEncounter.ChangeStatus to Closed). The optional discharge note is written as a
    // ClinicalNote of type Discharge in the same transaction.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CloseEncounterRequest(string? DischargeNote = null);

    public sealed record ClinicalNoteSummary(
        Guid Id,
        Guid EncounterId,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicalNoteType Type,
        string Content,
        DateTimeOffset OccurredAtUtc,
        DateTimeOffset CreatedAtUtc,
        Guid AuthorStaffId,
        string? AuthorDisplayName);

    public sealed record DiagnosisSummary(
        Guid Id,
        Guid EncounterId,
        string Description,
        string? CodeSystem,
        string? Code,
        [property: JsonConverter(typeof(TextEnumConverter))] DiagnosisType Type,
        bool IsPrimary,
        DateTimeOffset RecordedAtUtc,
        DateTimeOffset? ConfirmedAtUtc,
        Guid RecordedByStaffId);
}
