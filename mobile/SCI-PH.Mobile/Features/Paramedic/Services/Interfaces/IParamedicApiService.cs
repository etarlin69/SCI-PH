using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

namespace SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

public interface IParamedicApiService
{
    Task<EmergencyDto> CreateEmergencyAsync(CreateEmergencyDto dto);
    Task<EmergencyDto?> GetEmergencyAsync(Guid id);

    /// <summary>La misma emergencia ya identificada, o null si ningún paciente tiene ese documento (sigue NN).</summary>
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
