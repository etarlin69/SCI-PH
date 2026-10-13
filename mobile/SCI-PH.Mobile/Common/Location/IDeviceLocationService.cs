namespace SCI_PH.Mobile.Common.Location;

/// <summary>Coordenadas del dispositivo en un momento puntual (no hay seguimiento).</summary>
public sealed record DeviceLocation(double Latitude, double Longitude);

/// <summary>
/// Ubicación de mejor esfuerzo: devuelve null si no hay permiso, GPS o una posición conocida; nunca lanza.
/// Sin seguimiento continuo ni ubicación en segundo plano.
/// </summary>
public interface IDeviceLocationService
{
    Task<DeviceLocation?> TryGetLocationAsync();
}
