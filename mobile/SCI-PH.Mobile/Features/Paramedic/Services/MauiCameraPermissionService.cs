using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.Services;

/// <summary>
/// Permiso de cámara con las API de MAUI. Android e iOS piden el permiso del sistema; en Windows (app sin empaquetar) no hay
/// diálogo: se comprueba abriendo la cámara un instante, y si no hay cámara o la privacidad la bloquea queda "no disponible".
/// En Mac Catalyst no hay escáner: ni se declara ni se pide la cámara.
/// </summary>
public sealed class MauiCameraPermissionService : ICameraPermissionService
{
#if ANDROID || IOS || WINDOWS
    public bool IsScannerSupported => true;
#else
    public bool IsScannerSupported => false;
#endif

    public async Task<CameraPermissionStatus> CheckAsync()
    {
#if WINDOWS
        return await ProbeWindowsCameraAsync();
#elif ANDROID || IOS
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
#if IOS
        // iOS pregunta una sola vez: después de un "no", solo la configuración lo cambia.
        if (status == PermissionStatus.Denied)
        {
            return CameraPermissionStatus.DeniedPermanently;
        }
#endif
        return Map(status);
#else
        await Task.CompletedTask;
        return CameraPermissionStatus.Unavailable;
#endif
    }

    public async Task<CameraPermissionStatus> RequestAsync()
    {
#if WINDOWS
        return await ProbeWindowsCameraAsync();
#elif ANDROID || IOS
        var status = await Permissions.RequestAsync<Permissions.Camera>();
        if (status is PermissionStatus.Granted or PermissionStatus.Limited)
        {
            return CameraPermissionStatus.Granted;
        }
#if ANDROID
        // Tras pedirlo, sin explicación posible: el usuario eligió no volver a preguntar.
        return Permissions.ShouldShowRationale<Permissions.Camera>()
            ? CameraPermissionStatus.Denied
            : CameraPermissionStatus.DeniedPermanently;
#else
        return status == PermissionStatus.Restricted ? CameraPermissionStatus.Unavailable : CameraPermissionStatus.DeniedPermanently;
#endif
#else
        await Task.CompletedTask;
        return CameraPermissionStatus.Unavailable;
#endif
    }

    public void OpenSettings()
    {
#if ANDROID || IOS
        AppInfo.Current.ShowSettingsUI();
#endif
    }

#if ANDROID || IOS
    private static CameraPermissionStatus Map(PermissionStatus status) => status switch
    {
        PermissionStatus.Granted or PermissionStatus.Limited => CameraPermissionStatus.Granted,
        PermissionStatus.Restricted => CameraPermissionStatus.Unavailable,
        _ => CameraPermissionStatus.Denied
    };
#endif

#if WINDOWS
    // Sin cámara, o con la cámara bloqueada para las apps de escritorio, la inicialización falla: nunca un cierre de la app.
    private static async Task<CameraPermissionStatus> ProbeWindowsCameraAsync()
    {
        try
        {
            var devices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(
                Windows.Devices.Enumeration.DeviceClass.VideoCapture);
            if (devices.Count == 0)
            {
                return CameraPermissionStatus.Unavailable;
            }

            using var capture = new Windows.Media.Capture.MediaCapture();
            await capture.InitializeAsync(new Windows.Media.Capture.MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = Windows.Media.Capture.StreamingCaptureMode.Video
            });
            return CameraPermissionStatus.Granted;
        }
        catch (Exception)
        {
            return CameraPermissionStatus.Unavailable;
        }
    }
#endif
}
