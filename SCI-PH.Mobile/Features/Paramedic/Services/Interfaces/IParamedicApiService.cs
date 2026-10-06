using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SCI_PH.Mobile.Features.Paramedic.Models;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

namespace SCI_PH.Mobile.Features.Paramedic.Services.Interfaces
{
    public interface IParamedicApiService
    {
        Task<EmergencyDTO> CreateEmergencyAsync(CreateEmergencyDto dto);
        Task<EmergencyDTO?> GetEmergencyAsync(Guid id);
        Task<IdentifyPatientDto> IdentifyPatientAsync(Guid emergencyId);
        Task RegisterVitalsAsync(Guid emergencyId, RegisterVitalsDto dto);
    }
}
