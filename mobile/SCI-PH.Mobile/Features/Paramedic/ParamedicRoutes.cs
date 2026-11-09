using SCI_PH.Mobile.Common.Navigation;

namespace SCI_PH.Mobile.Features.Paramedic;

/// <summary>
/// Rutas de navegación del flujo paramédico (evita que los ViewModels dependan de las Views).
/// Ningún otro archivo escribe estas cadenas; se registran con
/// <see cref="ParamedicServiceCollectionExtensions.RegisterParamedicRoutes"/>.
/// </summary>
public static class ParamedicRoutes
{
    /// <summary>
    /// Inicio del paramédico: raíz del área (sin flecha "Atrás"), con "Nueva emergencia" y "Emergencias activas". Es un
    /// ShellContent de AppShell; el resto del flujo se apila sobre él.
    /// </summary>
    public const string Home = "ParamedicHome";

    /// <summary>El inicio como ruta absoluta: reemplaza la pila (login, MFA o el flujo de una emergencia dejan de estar detrás).</summary>
    public static string HomeRoot { get; } = AppRoutes.Root(Home);

    /// <summary>Formulario de una emergencia nueva, sobre el inicio.</summary>
    public const string NewEmergency = "NewEmergency";

    public const string IdentifyPatient = "IdentifyPatient";

    /// <summary>
    /// Parámetro de <see cref="IdentifyPatient"/> (bool): se abrió desde el traslado, así que al terminar vuelve a él en vez de
    /// avanzar a los signos vitales.
    /// </summary>
    public const string ReturnToTransfer = "ReturnToTransfer";

    /// <summary>Vuelve a la pantalla anterior de la pila (la que abrió la actual).</summary>
    public const string Back = "..";

    /// <summary>Escáner del QR del paciente, sobre la identificación (no se ofrece en Mac Catalyst).</summary>
    public const string ScanQr = "ScanQr";

    public const string VitalSigns = "VitalSigns";
    public const string Procedures = "Procedures";
    public const string Medications = "Medications";
    public const string Notes = "Notes";
    public const string Transfer = "Transfer";
}
