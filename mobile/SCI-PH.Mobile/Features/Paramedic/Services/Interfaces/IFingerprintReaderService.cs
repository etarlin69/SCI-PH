using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

namespace SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

/// <summary>
/// Frontera hacia la lectura de huella. La app NO habla con el ESP32/R307:
/// la implementación real consumirá el Device API / backend.
/// </summary>
public interface IFingerprintReaderService
{
    Task<FingerprintIdentifyResultDto> ReadAsync();
}
