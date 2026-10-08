using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.Services.Mocks;

/// <summary>
/// Simulador de huella. Alterna el resultado para probar ambos caminos sin tocar código:
/// 1.ª lectura = sin coincidencia, 2.ª = encontrado, 3.ª = sin coincidencia, ...
/// </summary>
public class MockFingerprintReaderService : IFingerprintReaderService
{
    private const int SimulatedDelayMs = 1200;
    private int _reads;

    public async Task<FingerprintIdentifyResultDto> ReadAsync()
    {
        await Task.Delay(SimulatedDelayMs);

        _reads++;
        var found = _reads % 2 == 0;

        return found
            ? new FingerprintIdentifyResultDto
            {
                Found = true,
                PatientId = Guid.NewGuid(),
                DisplayName = "Paciente Ficticio"
            }
            : new FingerprintIdentifyResultDto { Found = false };
    }
}
