namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>Procedimiento registrado en la emergencia (solo lo conserva el servicio simulado; el GET real aún no lo devuelve).</summary>
public class ProcedureDto
{
    public DateTime PerformedAt { get; set; }
    public string ProcedureCode { get; set; } = string.Empty;
    public string? Outcome { get; set; }
}
