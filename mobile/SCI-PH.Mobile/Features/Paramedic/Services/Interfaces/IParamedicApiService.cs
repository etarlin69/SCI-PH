using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

namespace SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

public interface IParamedicApiService
{
    Task<EmergencyDto> CreateEmergencyAsync(CreateEmergencyDto dto);
    Task<EmergencyDto?> GetEmergencyAsync(Guid id);

    /// <summary>
    /// Las emergencias que el paramédico actual aún puede continuar (asignadas y sin entregar), la más reciente primero. El
    /// servidor decide qué está activo; la app no guarda ni filtra esta lista.
    /// </summary>
    Task<IReadOnlyList<ActiveEmergencyDto>> GetActiveEmergenciesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// El candidato del documento (nombre, nacimiento, documento enmascarado), sin identificar la emergencia. El número viaja
    /// tal como se escribió y solo en el cuerpo. Ningún paciente con ese documento es un 404, como
    /// <see cref="Common.Api.MobileApiException"/>.
    /// </summary>
    Task<DocumentCandidateDto> ResolveDocumentCandidateAsync(
        Guid emergencyId, PatientDocumentType documentType, string documentNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// El paramédico confirmó el candidato del documento: la MISMA emergencia, identificada (el servidor vuelve a resolver el
    /// documento). Null si ningún paciente lo tiene ya (sigue NN). Repetirlo con la misma clave y el mismo documento devuelve la
    /// misma emergencia.
    /// </summary>
    Task<EmergencyDto?> IdentifyByDocumentAsync(Guid emergencyId, IdentifyByDocumentDto dto);

    /// <summary>
    /// El candidato del QR escaneado, sin identificar la emergencia. El contenido viaja solo en el cuerpo. Cualquier QR no
    /// válido (vencido, sustituido, revocado, desconocido) es el mismo 404, como <see cref="Common.Api.MobileApiException"/>.
    /// </summary>
    Task<QrCandidateDto> ResolveQrCandidateAsync(Guid emergencyId, string payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// El paramédico confirmó el candidato: la MISMA emergencia, identificada. El servidor vuelve a validar el QR. Repetirlo con
    /// la misma clave y el mismo QR devuelve la misma emergencia (también si el QR ya venció).
    /// </summary>
    Task<EmergencyDto> ConfirmQrIdentificationAsync(Guid emergencyId, Guid clientRequestId, string payload, CancellationToken cancellationToken = default);
    Task RegisterVitalsAsync(Guid emergencyId, RegisterVitalsDto dto);
    Task RegisterProcedureAsync(Guid emergencyId, RegisterProcedureDto dto);
    Task RegisterMedicationAsync(Guid emergencyId, RegisterMedicationDto dto);
    Task RegisterNoteAsync(Guid emergencyId, RegisterNoteDto dto);
    Task<TransferDto> RequestTransferAsync(Guid emergencyId, RequestTransferDto dto);
    /// <summary>Inicia ESE traslado (Requested → InTransit). Repetirlo sobre uno ya InTransit devuelve el mismo traslado.</summary>
    Task<TransferDto> StartTransferAsync(Guid emergencyId, Guid transferId);
    /// <summary>Cancela ESE traslado. Repetirlo sobre uno ya cancelado devuelve el mismo traslado.</summary>
    Task<TransferDto> CancelTransferAsync(Guid emergencyId, Guid transferId);
}
