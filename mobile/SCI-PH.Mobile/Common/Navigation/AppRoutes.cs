namespace SCI_PH.Mobile.Common.Navigation;

/// <summary>Rutas de la app que no pertenecen a una feature: la entrada compartida.</summary>
public static class AppRoutes
{
    /// <summary>Entrada compartida: el usuario elige personal clínico o paciente.</summary>
    public const string Entry = "Entry";

    /// <summary>Ruta absoluta (<c>//ruta</c>): reemplaza la pila de navegación.</summary>
    public static string Root(string route) => $"//{route}";
}
