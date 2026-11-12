using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

namespace SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

/// <summary>
/// Identificación por huella contra el Mobile BFF. La app NO habla con el ESP32/R307: abre una sesión con el lector
/// elegido, consulta su estado mientras el lector captura y, si hay coincidencia, confirma. Solo la confirmación
/// identifica la emergencia. Los fallos llegan como MobileApiException.
/// </summary>
public interface IFingerprintIdentificationService
{
    Task<IReadOnlyList<FingerprintReaderDto>> GetReadersAsync(CancellationToken cancellationToken = default);

    /// <summary>201 (nueva) o 200 (reintento con la misma clave y el mismo lector): la misma sesión.</summary>
    Task<FingerprintSessionDto> OpenSessionAsync(Guid emergencyId, Guid clientRequestId, Guid readerId, CancellationToken cancellationToken = default);

    Task<FingerprintSessionDto> GetSessionAsync(Guid emergencyId, Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>La emergencia ya identificada (también al reintentar con la misma clave).</summary>
    Task<EmergencyDto> ConfirmAsync(Guid emergencyId, Guid sessionId, Guid clientRequestId, CancellationToken cancellationToken = default);

    /// <summary>Repetirlo sobre una sesión ya terminada sin identificación devuelve su estado.</summary>
    Task<FingerprintSessionDto> CancelAsync(Guid emergencyId, Guid sessionId, CancellationToken cancellationToken = default);
}
