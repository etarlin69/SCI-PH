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
    public const string VitalSigns = "VitalSigns";
    public const string Procedures = "Procedures";
    public const string Medications = "Medications";
    public const string Notes = "Notes";
    // Próxima: Transfer
}
