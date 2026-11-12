namespace SCI_PH.Mobile.Features.Patient;

/// <summary>
/// Rutas Shell del módulo del paciente: las secciones del área del paciente, declaradas en el AppShell. Ningún otro
/// archivo escribe estas cadenas. Se entra tras el inicio de sesión común (<c>AuthRoutes.PatientArea</c>).
/// </summary>
public static class PatientRoutes
{
    public const string Home = "Home";
    public const string Profile = "Profile";
    public const string MyQr = "MyQr";
    public const string History = "History";
    public const string Results = "Results";
    public const string Documents = "Documents";

    /// <summary>Detalle de una atención, apilado sobre el historial (no es una sección del menú).</summary>
    public const string EncounterDetail = "EncounterDetail";

    /// <summary>Parámetro de navegación del detalle: el id de la atención elegida en el historial.</summary>
    public const string EncounterIdParameter = "EncounterId";
}
