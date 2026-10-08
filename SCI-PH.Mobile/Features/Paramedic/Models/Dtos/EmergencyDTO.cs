using SCI_PH.Mobile.Features.Paramedic.Models.Enums;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>
/// Respuesta de GET /api/mobile/emergencies/{id}.
/// Procedimientos, medicamentos y traslado se agregan cuando se construyan esas pantallas.
/// </summary>
public class EmergencyDto
{
    public Guid Id { get; set; }
    public EmergencyStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public PatientSummaryDto? Patient { get; set; }
    public List<VitalSignDto> Vitals { get; set; } = new();
}
