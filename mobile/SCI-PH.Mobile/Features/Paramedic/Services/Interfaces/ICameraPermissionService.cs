namespace SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

/// <summary>Estado del permiso de cámara para el escáner de QR del paramédico.</summary>
public enum CameraPermissionStatus
{
    Granted,

    /// <summary>Sin permiso todavía (o denegado una vez): se puede volver a pedir, solo cuando el paramédico lo decida.</summary>
    Denied,

    /// <summary>Denegado sin que el sistema vuelva a preguntar: solo desde la configuración de la app.</summary>
    DeniedPermanently,

    /// <summary>No hay cámara utilizable (o la privacidad del sistema la bloquea).</summary>
    Unavailable
}

/// <summary>
/// Envoltorio mínimo de los permisos de MAUI para el escáner de QR (las API estáticas no se pueden probar). No abstrae la
/// cámara: la vista de ZXing vive solo en la página. Solo el escáner del paramédico lo usa: el área del paciente nunca pide
/// la cámara.
/// </summary>
public interface ICameraPermissionService
{
    /// <summary>La plataforma ofrece el escáner (no en Mac Catalyst).</summary>
    bool IsScannerSupported { get; }

    /// <summary>Consulta sin preguntar al usuario.</summary>
    Task<CameraPermissionStatus> CheckAsync();

    /// <summary>Pide el permiso (el sistema puede mostrar su diálogo).</summary>
    Task<CameraPermissionStatus> RequestAsync();

    /// <summary>Abre la configuración de la app para activar el permiso.</summary>
    void OpenSettings();
}
