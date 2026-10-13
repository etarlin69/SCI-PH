namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>Procedimiento registrado, tal como lo devuelve el GET de la emergencia.</summary>
public class ProcedureDto
{
    public DateTime RecordedAt { get; set; }
    public string ProcedureCode { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
