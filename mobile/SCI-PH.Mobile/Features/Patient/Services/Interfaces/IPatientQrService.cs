using SCI_PH.Mobile.Features.Patient.Models.Dtos;

namespace SCI_PH.Mobile.Features.Patient.Services.Interfaces;

/// <summary>El QR del paciente autenticado (ADR-015). Identifica; nunca autentica ni da acceso por sí solo.</summary>
public interface IPatientQrService
{
    /// <summary>
    /// Pide un QR nuevo, que sustituye al anterior del paciente en cualquier dispositivo: el servidor solo guarda su hash y
    /// no puede volver a entregar uno ya emitido.
    /// </summary>
    Task<PatientQrDto> IssueAsync(CancellationToken cancellationToken = default);
}
