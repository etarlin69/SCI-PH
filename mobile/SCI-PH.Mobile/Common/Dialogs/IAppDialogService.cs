namespace SCI_PH.Mobile.Common.Dialogs;

/// <summary>Avisos al usuario desde los ViewModels, sin depender de <c>Shell.Current</c>.</summary>
public interface IAppDialogService
{
    Task ShowAsync(string title, string message, string accept = "OK");
}
