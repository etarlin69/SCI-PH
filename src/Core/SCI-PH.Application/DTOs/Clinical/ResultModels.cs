using System.Text.Json.Serialization;
using SCI_PH.Application.Common.Serialization;
using SCI_PH.Domain.Enums.Encounters;

namespace SCI_PH.Application.DTOs.Clinical
{
    // A result of an encounter as staff see it (all statuses). The recorder's name is resolved like a note's author.
    public sealed record ClinicalResultSummary(
        Guid Id,
        Guid EncounterId,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicalResultType Type,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicalResultStatus Status,
        string Name,
        string? CodeSystem,
        string? Code,
        string? ResultText,
        decimal? NumericValue,
        string? Unit,
        string? ReferenceRange,
        DateTimeOffset? ResultedAtUtc,
        string? RecordedByDisplayName);

    // ClinicalResult.Request: an order waiting for its value (Pending).
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RequestClinicalResultRequest(
        [property: JsonRequired, JsonConverter(typeof(TextEnumConverter))] ClinicalResultType Type,
        [property: JsonRequired] string Name,
        string? CodeSystem = null,
        string? Code = null,
        string? ReferenceRange = null);

    // RecordFinal (Pending -> Final) or Correct (Final/Corrected -> Corrected): a text and/or a numeric value.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RecordClinicalResultRequest(
        string? ResultText,
        decimal? NumericValue,
        string? Unit,
        DateTimeOffset? ResultedAtUtc = null);
}
