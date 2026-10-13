namespace SCI_PH.PatientPrototype.Features.Patient;

/// <summary>Rutas Shell del módulo del paciente. Ningún otro archivo escribe estas cadenas.</summary>
public static class PatientRoutes
{
    public const string QrAccess = "QrAccess";
    public const string FaceVerify = "FaceVerify";
    public const string Home = "Home";
    public const string Profile = "Profile";
    public const string History = "History";
    public const string Results = "Results";
    public const string Documents = "Documents";
    public const string Assistant = "Assistant";

    /// <summary>Ruta absoluta (<c>//ruta</c>): reemplaza la pila de navegación.</summary>
    public static string Root(string route) => $"//{route}";
}
