namespace SCI_PH.Mobile.Features.Patient;

/// <summary>
/// Rutas Shell del módulo del paciente: las secciones del área del paciente, declaradas en el AppShell. Ningún otro
/// archivo escribe estas cadenas. Se entra tras el inicio de sesión común (<c>AuthRoutes.PatientArea</c>).
/// </summary>
public static class PatientRoutes
{
    public const string Home = "Home";
    public const string Profile = "Profile";
    public const string History = "History";
    public const string Results = "Results";
    public const string Documents = "Documents";
}
