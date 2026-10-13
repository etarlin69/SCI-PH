using SCI_PH.Mobile.Features.Paramedic.Models.Enums;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>
/// Body de POST /api/mobile/emergencies/{id}/identify.
/// Fingerprint: la huella la resuelve el Device API; aquí solo se vincula el PatientId resultante.
/// Los campos de Manual están pendientes con Rol 2.
/// </summary>
public class IdentifyEmergencyDto
{
    public Guid ClientRequestId { get; set; } = Guid.NewGuid();
    public IdentificationMethod Method { get; set; }
    public Guid? PatientId { get; set; }
    public DocumentDto? Document { get; set; }
}
