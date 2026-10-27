namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>
/// Una observación del paramédico, POST /api/mobile/emergencies/{id}/notes. El tipo (Prehospital) y el autor los pone el
/// servidor. OccurredAt (UTC, automático y no editable) y ClientRequestId se generan una vez por nota y se conservan en los
/// reintentos.
/// </summary>
public class RegisterNoteDto
{
    public Guid ClientRequestId { get; set; } = Guid.NewGuid();
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string Content { get; set; } = string.Empty;
}
