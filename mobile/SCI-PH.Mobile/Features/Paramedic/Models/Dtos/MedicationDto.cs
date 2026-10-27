using SCI_PH.Mobile.Features.Paramedic.Models.Enums;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>Dosis registrada en la emergencia (solo la conserva el servicio simulado; el GET real aún no la devuelve).</summary>
public class MedicationDto
{
    public DateTime AdministeredAt { get; set; }
    public Guid MedicationId { get; set; }
    public decimal Dose { get; set; }
    public string DoseUnit { get; set; } = string.Empty;
    public MedicationRoute Route { get; set; }
    public string? Notes { get; set; }
}
