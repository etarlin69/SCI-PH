using SCI_PH.Mobile.Features.Paramedic.Models.Enums;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>Clínica activa a la que se puede trasladar al paciente (GET /api/mobile/catalogs/transfer-destinations).</summary>
public class TransferDestinationDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Tipo de clínica tal como lo envía el servidor (Hospital, Clinic, EmergencyCenter...).</summary>
    public string Type { get; set; } = string.Empty;
    public string Municipality { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;

    /// <summary>Nombre, tipo y lugar, para distinguir destinos.</summary>
    public string DisplayName => $"{Name} · {TypeLabel} · {Municipality}, {Province}";

    public string TypeLabel => Type switch
    {
        "Hospital" => "Hospital",
        "Clinic" => "Clínica",
        "PrimaryCareCenter" => "Atención primaria",
        "EmergencyCenter" => "Centro de emergencias",
        _ => "Otro"
    };
}

/// <summary>Traslado de la emergencia tal como lo devuelve el Mobile BFF (también como activeTransfer del GET).</summary>
public class TransferDto
{
    public Guid Id { get; set; }
    public TransferStatus Status { get; set; }
    public TransferDestinationDto Destination { get; set; } = new();
    public DateTime RequestedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}

/// <summary>
/// Solicitud de traslado, POST /api/mobile/emergencies/{id}/transfers. Sin origen, horas ni personal: los pone el
/// servidor. ClientRequestId se genera una vez por solicitud y se conserva en los reintentos.
/// </summary>
public class RequestTransferDto
{
    public Guid ClientRequestId { get; set; } = Guid.NewGuid();
    public Guid DestinationClinicId { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
}
