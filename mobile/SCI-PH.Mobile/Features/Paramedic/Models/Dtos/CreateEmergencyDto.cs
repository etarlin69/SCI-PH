namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>Body de POST /api/mobile/emergencies.</summary>
public class CreateEmergencyDto
{
    /// <summary>Generado en el dispositivo; permite reintentar sin duplicar.</summary>
    public Guid ClientRequestId { get; set; } = Guid.NewGuid();

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Coordenadas; null si no hay GPS/permiso.</summary>
    public LocationDto? Location { get; set; }

    public string ChiefComplaint { get; set; } = string.Empty;
}
