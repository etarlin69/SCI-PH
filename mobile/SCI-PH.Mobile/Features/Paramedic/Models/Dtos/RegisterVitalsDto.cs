namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>
/// Una toma de POST /api/mobile/emergencies/{id}/vital-signs: todos los valores son opcionales individualmente, pero
/// hace falta al menos uno; solo se envían los capturados. Límites de plausibilidad (dominio, VitalSignLimits):
/// FC 0-300, FR 0-100, sistólica 0-300, diastólica 0-250, SpO2 0-100, Temp 25-45 °C (máx. 2 decimales), Glasgow 3-15.
/// RecordedAt (UTC) y ClientRequestId se generan una vez por toma y se conservan en los reintentos.
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
