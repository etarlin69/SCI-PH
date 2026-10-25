using SCI_PH.Mobile.Features.Paramedic.Models.Enums;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>
/// Body de POST /api/mobile/emergencies/{id}/identify/document. El número se envía tal como se escribió: el servidor lo
/// normaliza. ClientRequestId es propio de la identificación (no el de la apertura) y se conserva entre reintentos.
/// </summary>
public class IdentifyByDocumentDto
{
    public Guid ClientRequestId { get; set; }
    public PatientDocumentType DocumentType { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
}
