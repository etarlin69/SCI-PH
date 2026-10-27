using System.Text.Json.Serialization;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Enums;

/// <summary>
/// Estado de una sesión de huella: los nombres de FingerprintSessionStatus del servidor, que viajan como texto.
/// Pending espera al lector; Matched espera la confirmación del paramédico; el resto termina la sesión.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FingerprintSessionStatus>))]
public enum FingerprintSessionStatus
{
    Pending,
    Matched,
    NoMatch,
    Failed,
    Expired,
    Cancelled,
    Confirmed
}

/// <summary>Por qué falló la lectura (solo con Failed): calidad insuficiente o lector no disponible.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FingerprintFailure>))]
public enum FingerprintFailure
{
    LowQuality,
    SensorError
}
