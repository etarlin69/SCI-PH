namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>
/// Resultado de la lectura de huella (la resuelve el Device API).
/// PENDIENTE: confirmar con Rol 1 / Rol 9 cómo llega este resultado a la app
/// (docs/api/device-api-contracts.md: 200 con identificador y datos mínimos; 404 sin coincidencia).
/// </summary>
public class FingerprintIdentifyResultDto
{
    public bool Found { get; set; }
    public Guid? PatientId { get; set; }
    public string? DisplayName { get; set; }
}
