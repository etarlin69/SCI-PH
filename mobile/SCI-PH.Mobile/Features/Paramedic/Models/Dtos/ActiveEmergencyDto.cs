using SCI_PH.Mobile.Features.Paramedic.Models.Enums;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>
/// Una emergencia de "Emergencias activas" (GET /api/mobile/emergencies/active): solo lo que muestra la lista. Para retomarla
/// la app vuelve a leer la emergencia; esta fila nunca decide la pantalla a la que se entra.
/// </summary>
public class ActiveEmergencyDto
{
    public Guid Id { get; set; }

    /// <summary>Código NN del servidor; se conserva tras la identificación.</summary>
    public string? TemporaryPatientCode { get; set; }

    /// <summary>Null mientras el paciente es NN.</summary>
    public string? PatientDisplayName { get; set; }

    /// <summary>Estado de la atención tal como lo envía el servidor (Created, InProgress, Transferred...).</summary>
    public string Status { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }

    public string? ChiefComplaint { get; set; }

    /// <summary>Requested o InTransit; null sin traslado activo.</summary>
    public TransferStatus? ActiveTransferStatus { get; set; }
}
