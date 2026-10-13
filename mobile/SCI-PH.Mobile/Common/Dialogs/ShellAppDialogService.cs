namespace SCI_PH.Mobile.Common.Dialogs;

public sealed class ShellAppDialogService : IAppDialogService
{
    public Task ShowAsync(string title, string message, string accept = "OK") =>
        Shell.Current.DisplayAlert(title, message, accept);
}
