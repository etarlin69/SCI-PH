namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>Body de POST /api/mobile/emergencies/{id}/procedures.</summary>
public class RegisterProcedureDto
{
    public Guid ClientRequestId { get; set; } = Guid.NewGuid();
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Referencia un catálogo administrado por Administración (Rol 5).</summary>
    public string ProcedureCode { get; set; } = string.Empty;

    public string? Notes { get; set; }
}
