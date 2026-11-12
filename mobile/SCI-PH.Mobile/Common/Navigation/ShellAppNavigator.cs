namespace SCI_PH.Mobile.Common.Navigation;

public sealed class ShellAppNavigator : IAppNavigator
{
    public Task GoToAsync(string route) => Shell.Current.GoToAsync(route);

    public Task GoToAsync(string route, IDictionary<string, object> parameters) =>
        Shell.Current.GoToAsync(route, parameters);
}
