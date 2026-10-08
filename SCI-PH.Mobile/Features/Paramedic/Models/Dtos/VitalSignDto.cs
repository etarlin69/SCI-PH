namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>Registro de signos vitales tal como lo devuelve el GET de la emergencia.</summary>
public class VitalSignDto
{
    public DateTime RecordedAt { get; set; }
    public int? HeartRate { get; set; }
    public int? RespiratoryRate { get; set; }
    public int? SystolicPressure { get; set; }
    public int? DiastolicPressure { get; set; }
    public int? OxygenSaturation { get; set; }
    public decimal? Temperature { get; set; }
    public int? Glasgow { get; set; }
}
