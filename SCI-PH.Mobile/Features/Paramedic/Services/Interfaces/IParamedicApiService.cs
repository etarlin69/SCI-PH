using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

namespace SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

public interface IParamedicApiService
{
    Task<EmergencyDto> CreateEmergencyAsync(CreateEmergencyDto dto);
    Task<EmergencyDto?> GetEmergencyAsync(Guid id);
    Task<EmergencyDto> IdentifyAsync(Guid emergencyId, IdentifyEmergencyDto dto);
    Task RegisterVitalsAsync(Guid emergencyId, RegisterVitalsDto dto);
}
