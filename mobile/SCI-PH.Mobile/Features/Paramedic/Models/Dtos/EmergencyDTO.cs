using SCI_PH.Mobile.Features.Paramedic.Models.Enums;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>
/// Respuesta de GET /api/mobile/emergencies/{id}.
/// El traslado se agrega cuando se construya esa pantalla.
/// </summary>
public class EmergencyDto
{
    public Guid Id { get; set; }
    public EmergencyStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public PatientSummaryDto? Patient { get; set; }
    public List<VitalSignDto> Vitals { get; set; } = new();
    public List<ProcedureDto> Procedures { get; set; } = new();
    public List<MedicationDto> Medications { get; set; } = new();
}
