using System.Text.Json.Serialization;
using SCI_PH.Application.Common.Serialization;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Transport;

namespace SCI_PH.Application.DTOs.Clinical
{
    // ClinicalTransfer.Request from the encounter's clinic. The origin is the encounter's clinic, never the client's.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record RequestTransferRequest(
        [property: JsonRequired] Guid DestinationClinicId,
        string? Reason = null,
        string? Notes = null);

    public sealed record AmbulanceSummary(Guid Id, string Code, string? CallSign);

    public sealed record ClinicalTransferSummary(
        Guid Id,
        Guid EncounterId,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicalTransferStatus Status,
        ClinicSummary? OriginClinic,
        ClinicSummary DestinationClinic,
        AmbulanceSummary? Ambulance,
        DateTimeOffset RequestedAtUtc,
        DateTimeOffset? SentAtUtc,
        Guid? SentByStaffId,
        DateTimeOffset? ReceivedAtUtc,
        Guid? ReceivedByStaffId,
        DateTimeOffset? CancelledAtUtc,
        PatientSummary? Patient,
        string? TemporaryPatientCode,
        string? Reason,
        string? Notes,
        [property: JsonConverter(typeof(TextEnumConverter))] TriagePriority? LatestTriagePriority,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicalEncounterStatus EncounterStatus);

    // Read model of a transfer with its encounter's subject (unmasked: Application masks it).
    public sealed record TransferRow(
        Guid Id,
        Guid EncounterId,
        ClinicalTransferStatus Status,
        ClinicSummary? OriginClinic,
        ClinicSummary DestinationClinic,
        AmbulanceSummary? Ambulance,
        DateTimeOffset RequestedAtUtc,
        DateTimeOffset? SentAtUtc,
        Guid? SentByStaffId,
        DateTimeOffset? ReceivedAtUtc,
        Guid? ReceivedByStaffId,
        DateTimeOffset? CancelledAtUtc,
        PatientRow? Patient,
        string? TemporaryPatientCode,
        string? Reason,
        string? Notes,
        TriagePriority? LatestTriagePriority,
        ClinicalEncounterStatus EncounterStatus);
}
