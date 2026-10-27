using SCI_PH.Mobile.Features.Paramedic.Models.Enums;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>
/// Una dosis YA ADMINISTRADA, POST /api/mobile/emergencies/{id}/medication-administrations. SCI-PH no prescribe: no hay
/// prescripción. MedicationId es un medicamento activo del catálogo del servidor; el nombre lo pone el servidor.
/// DoseUnit es texto libre (1 a 24 caracteres): la lista de la app es solo una ayuda. AdministeredAt (UTC) y
/// ClientRequestId se generan una vez por dosis y se conservan en los reintentos.
/// </summary>
public class RegisterMedicationDto
{
    public Guid ClientRequestId { get; set; } = Guid.NewGuid();
    public DateTime AdministeredAt { get; set; } = DateTime.UtcNow;
    public Guid MedicationId { get; set; }
    public decimal Dose { get; set; }
    public string DoseUnit { get; set; } = string.Empty;
    public MedicationRoute Route { get; set; }
    public string? Notes { get; set; }
}
