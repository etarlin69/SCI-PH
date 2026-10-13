namespace SCI_PH.Mobile.Common.Location;

/// <summary>
/// Pide el permiso "mientras se usa la app" la primera vez y toma la última posición conocida, que es inmediata.
/// </summary>
public sealed class MauiDeviceLocationService : IDeviceLocationService
{
    private readonly IGeolocation _geolocation;

    public MauiDeviceLocationService(IGeolocation geolocation)
    {
        _geolocation = geolocation;
    }

    public async Task<DeviceLocation?> TryGetLocationAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.LocationWhenInUse>);
            }

            if (status != PermissionStatus.Granted)
            {
                return null;
            }

            var location = await _geolocation.GetLastKnownLocationAsync();
            return location is null ? null : new DeviceLocation(location.Latitude, location.Longitude);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
