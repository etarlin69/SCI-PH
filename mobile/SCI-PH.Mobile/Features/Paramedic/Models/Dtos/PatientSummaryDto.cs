namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>
/// Paciente vinculado a la emergencia (null en el GET mientras no esté identificada).
/// PENDIENTE: el contrato no define la forma; confirmar con Rol 1 / Rol 2.
/// </summary>
public class PatientSummaryDto
{
    public Guid Id { get; set; }
    public string? DisplayName { get; set; }
}
