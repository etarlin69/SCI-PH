namespace SCI_PH.Mobile.Features.Patient;

/// <summary>
/// Rutas Shell del módulo del paciente. Ningún otro archivo escribe estas cadenas. QrAccess y FaceVerify se apilan
/// sobre la entrada (<see cref="PatientServiceCollectionExtensions.RegisterPatientRoutes"/>); el resto son las
/// secciones del área del paciente, declaradas en el AppShell.
/// </summary>
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
}
