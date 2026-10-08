using System.Text.Json.Serialization;
using SCI_PH.Application.Common.Serialization;
using SCI_PH.Domain.Enums.Encounters;

namespace SCI_PH.Application.DTOs.Clinical
{
    // No unit: VitalSign.Record derives it from VitalSignLimits. Unknown members (such as "unit") are rejected.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record VitalSignMeasurementInput(
        [property: JsonRequired, JsonConverter(typeof(TextEnumConverter))] VitalSignType Type,
        [property: JsonRequired] decimal Value,
        [property: JsonRequired] DateTimeOffset MeasuredAtUtc);

    // The professional comes from the session. PerformedAtUtc defaults to now.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RecordTriageRequest(
        [property: JsonRequired, JsonConverter(typeof(TextEnumConverter))] TriagePriority Priority,
        string? Reason = null,
        string? Observations = null,
        DateTimeOffset? PerformedAtUtc = null,
        IReadOnlyList<VitalSignMeasurementInput>? Measurements = null);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RecordVitalSignRequest(
        [property: JsonRequired, JsonConverter(typeof(TextEnumConverter))] VitalSignType Type,
        [property: JsonRequired] decimal Value,
        [property: JsonRequired] DateTimeOffset MeasuredAtUtc);

    public sealed record TriageSummary(
        Guid Id,
        Guid EncounterId,
        [property: JsonConverter(typeof(TextEnumConverter))] TriagePriority Priority,
        string? Reason,
        string? Observations,
        DateTimeOffset PerformedAtUtc,
        Guid PerformedByStaffId);

    public sealed record VitalSignSummary(
        Guid Id,
        Guid EncounterId,
        Guid? TriageId,
        [property: JsonConverter(typeof(TextEnumConverter))] VitalSignType Type,
        decimal Value,
        string Unit,
        DateTimeOffset MeasuredAtUtc,
        Guid? MeasuredByStaffId);
}
