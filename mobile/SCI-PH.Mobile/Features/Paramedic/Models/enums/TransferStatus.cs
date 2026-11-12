using System.Text.Json.Serialization;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Enums;

/// <summary>Estado del traslado: los nombres de ClinicalTransferStatus del servidor, que viajan como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TransferStatus>))]
public enum TransferStatus
{
    Requested,
    InTransit,
    Received,
    Completed,
    Cancelled
}
