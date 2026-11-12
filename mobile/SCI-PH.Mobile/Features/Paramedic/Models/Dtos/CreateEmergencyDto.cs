namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>
/// Apertura de una emergencia (POST /api/mobile/emergencies). La ubicación queda fuera de V1: ni se toma ni se envía
/// (ver docs/adr/ADR-008-prehospital-emergency.md).
/// </summary>
public class CreateEmergencyDto
{
    /// <summary>Generado en el dispositivo una vez por emergencia y reutilizado en cada reintento (idempotencia).</summary>
    public Guid ClientRequestId { get; set; } = Guid.NewGuid();

    /// <summary>Inicio de la atención en UTC.</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public string ChiefComplaint { get; set; } = string.Empty;
}
