namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>
/// Un procedimiento ya realizado, POST /api/mobile/emergencies/{id}/procedures. ProcedureCode es un código del catálogo
/// del servidor (GET /api/mobile/catalogs/procedures): el nombre y la descripción los pone el servidor. PerformedAt (UTC) y
/// ClientRequestId se generan una vez por procedimiento y se conservan en los reintentos. Outcome: resultado, opcional.
/// </summary>
public class RegisterProcedureDto
{
    public Guid ClientRequestId { get; set; } = Guid.NewGuid();
    public DateTime PerformedAt { get; set; } = DateTime.UtcNow;
    public string ProcedureCode { get; set; } = string.Empty;
    public string? Outcome { get; set; }
}
