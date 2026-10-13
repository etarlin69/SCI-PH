namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>
/// Body de POST /api/mobile/emergencies/{id}/medications.
/// Registra lo ADMINISTRADO por el paramédico. SCI-PH no prescribe.
/// </summary>
public class RegisterMedicationDto
{
    public Guid ClientRequestId { get; set; } = Guid.NewGuid();
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public string MedicationCode { get; set; } = string.Empty;
    public string Dose { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
