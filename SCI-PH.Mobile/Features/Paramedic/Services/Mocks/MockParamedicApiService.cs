using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SCI_PH.Mobile.Features.Paramedic.Models;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.enums;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.Services.Mocks
{
    public class MockParamedicApiService : IParamedicApiService
    {
        private const int SimulatedDelayMs = 500;

        // "Base de datos" en memoria, solo vive mientras la app esté abierta
        private readonly Dictionary<Guid, EmergencyDTO> _emergencies = new();
        private readonly Dictionary<Guid, List<RegisterVitalsDto>> _vitals = new();

        public async Task<EmergencyDTO> CreateEmergencyAsync(CreateEmergencyDto dto)
        {
            await Task.Delay(SimulatedDelayMs);

            var emergency = new EmergencyDTO
            {
                Id = Guid.NewGuid(),
                Description = dto.Description,
                Location = dto.Location,
                CreatedAt = DateTime.UtcNow,
                Status = EmergencyStatus.Created,
                PatientId = null
            };

            _emergencies[emergency.Id] = emergency;
            return emergency;
        }

        public async Task<EmergencyDTO?> GetEmergencyAsync(Guid id)
        {
            await Task.Delay(SimulatedDelayMs);

            return _emergencies.TryGetValue(id, out var emergency)
                ? emergency
                : null;
        }

        public async Task<IdentifyPatientDto> IdentifyPatientAsync(Guid emergencyId)
        {
            await Task.Delay(SimulatedDelayMs);

            if (!_emergencies.TryGetValue(emergencyId, out var emergency))
                throw new InvalidOperationException("Emergencia no encontrada.");

            // Simulación: alterna entre encontrado y no encontrado
            // para poder probar la pantalla de identificación alternativa.
            var found = Random.Shared.Next(0, 2) == 1;

            if (!found)
                return new IdentifyPatientDto { Found = false };

            var patientId = Guid.NewGuid();
            emergency.PatientId = patientId;
            emergency.Status = EmergencyStatus.PatientIdentified;

            return new IdentifyPatientDto
            {
                Found = true,
                PatientId = patientId,
                DisplayName = "Paciente Ficticio"
            };
        }

        public async Task RegisterVitalsAsync(Guid emergencyId, RegisterVitalsDto dto)
        {
            await Task.Delay(SimulatedDelayMs);

            if (!_emergencies.TryGetValue(emergencyId, out var emergency))
                throw new InvalidOperationException("Emergencia no encontrada.");

            if (!_vitals.TryGetValue(emergencyId, out var list))
            {
                list = new List<RegisterVitalsDto>();
                _vitals[emergencyId] = list;
            }

            list.Add(dto);
            emergency.Status = EmergencyStatus.InCare;
        }
    }
}
