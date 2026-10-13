namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>
/// Body de POST /api/mobile/emergencies/{id}/vitals.
/// Todos los valores son opcionales individualmente. Límites de plausibilidad (dominio, VitalSignLimits):
/// FC 0-300, FR 0-100, sistólica 0-300, diastólica 0-250, SpO2 0-100, Temp 25-45 °C, Glasgow 3-15.
/// </summary>
public class RegisterVitalsDto
{
    public Guid ClientRequestId { get; set; } = Guid.NewGuid();
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public int? HeartRate { get; set; }
    public int? RespiratoryRate { get; set; }
    public int? SystolicPressure { get; set; }
    public int? DiastolicPressure { get; set; }
    public int? OxygenSaturation { get; set; }
    public decimal? Temperature { get; set; }
    public int? Glasgow { get; set; }
}
