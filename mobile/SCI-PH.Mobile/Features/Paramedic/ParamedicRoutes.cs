namespace SCI_PH.Mobile.Features.Paramedic;

/// <summary>
/// Rutas de navegación del flujo paramédico (evita que los ViewModels dependan de las Views).
/// Ningún otro archivo escribe estas cadenas; se registran con
/// <see cref="ParamedicServiceCollectionExtensions.RegisterParamedicRoutes"/>.
/// </summary>
public static class ParamedicRoutes
{
    /// <summary>Inicio del flujo; se apila sobre la entrada compartida ("Atrás" vuelve a elegir el acceso).</summary>
    public const string NewEmergency = "NewEmergency";

    public const string IdentifyPatient = "IdentifyPatient";

    /// <summary>Escáner del QR del paciente, sobre la identificación (no se ofrece en Mac Catalyst).</summary>
    public const string ScanQr = "ScanQr";

    public const string VitalSigns = "VitalSigns";
    public const string Procedures = "Procedures";
    public const string Medications = "Medications";
    public const string Notes = "Notes";
    public const string Transfer = "Transfer";
}
