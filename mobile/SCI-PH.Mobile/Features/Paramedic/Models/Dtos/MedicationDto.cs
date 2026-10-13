namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>Medicamento administrado, tal como lo devuelve el GET de la emergencia.</summary>
public class MedicationDto
{
    public DateTime RecordedAt { get; set; }
    public string MedicationCode { get; set; } = string.Empty;
    public string Dose { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
