using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

namespace SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

public interface IParamedicApiService
{
    Task<EmergencyDto> CreateEmergencyAsync(CreateEmergencyDto dto);
    Task<EmergencyDto?> GetEmergencyAsync(Guid id);
    Task<EmergencyDto> IdentifyAsync(Guid emergencyId, IdentifyEmergencyDto dto);

    /// <summary>La misma emergencia ya identificada, o null si ningún paciente tiene ese documento (sigue NN).</summary>
    Task<EmergencyDto?> IdentifyByDocumentAsync(Guid emergencyId, IdentifyByDocumentDto dto);
    Task RegisterVitalsAsync(Guid emergencyId, RegisterVitalsDto dto);
    Task RegisterProcedureAsync(Guid emergencyId, RegisterProcedureDto dto);
    Task RegisterMedicationAsync(Guid emergencyId, RegisterMedicationDto dto);
    Task RegisterNoteAsync(Guid emergencyId, RegisterNoteDto dto);
}
