using CommunityToolkit.Mvvm.ComponentModel;

namespace SCI_PH.Mobile.Common.ViewModels;

/// <summary>
/// Estado común de los ViewModels: operación en curso y un mensaje de error apto para el usuario.
/// </summary>
public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnIsBusyChanged(bool value) => OnBusyChanged();

    /// <summary>Para que cada ViewModel reevalúe sus comandos cuando cambia <see cref="IsBusy"/>.</summary>
    protected virtual void OnBusyChanged()
    {
    }

    /// <summary>
    /// Ejecuta una operación marcando <see cref="IsBusy"/>. Si falla, muestra <paramref name="errorText"/>:
    /// nunca el detalle de la excepción.
    /// </summary>
    protected async Task RunAsync(Func<Task> action, string errorText)
    {
        ErrorMessage = null;
        IsBusy = true;

        try
        {
            await action();
        }
        catch (Exception)
        {
            ErrorMessage = errorText;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
