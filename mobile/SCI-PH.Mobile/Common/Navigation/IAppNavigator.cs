namespace SCI_PH.Mobile.Common.Navigation;

/// <summary>
/// Navegación para los ViewModels de cualquier feature, sin depender de <c>Shell.Current</c>.
/// Las rutas salen de las constantes de cada feature (por ejemplo <c>ParamedicRoutes</c>).
/// </summary>
public interface IAppNavigator
{
    Task GoToAsync(string route);

    Task GoToAsync(string route, IDictionary<string, object> parameters);
}
