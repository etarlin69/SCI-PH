using SCI_PH.Mobile.Features.Paramedic.Models.Enums;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>
/// Respuesta de GET /api/mobile/emergencies/{id}, con el traslado activo (Requested o InTransit) si lo hay.
/// </summary>
public class EmergencyDto
{
    public Guid Id { get; set; }
    /// <summary>Código NN que asigna el servidor mientras el paciente no está identificado.</summary>
    public string? TemporaryPatientCode { get; set; }
    public EmergencyStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public PatientSummaryDto? Patient { get; set; }

    /// <summary>Traslado solicitado o en curso, para restaurar la pantalla de traslado; null si no hay.</summary>
    public TransferDto? ActiveTransfer { get; set; }
    public List<VitalSignDto> Vitals { get; set; } = new();
    public List<ProcedureDto> Procedures { get; set; } = new();
    public List<MedicationDto> Medications { get; set; } = new();
}
