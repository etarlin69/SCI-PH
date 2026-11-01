using CommunityToolkit.Mvvm.ComponentModel;

namespace SCI_PH.Mobile.Common.ViewModels;

/// <summary>
/// Estado común de los ViewModels: operación en curso y un mensaje de error apto para el usuario.
/// </summary>
public abstract partial class BaseViewModel : ObservableObject
{
    /// <summary>Mensaje para fallos sin un texto más concreto.</summary>
    public const string GenericErrorMessage = "No se pudo completar la operación. Inténtalo de nuevo.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool IsNotBusy => !IsBusy;

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
    protected Task RunAsync(Func<Task> action, string errorText) => RunAsync(action, _ => errorText);

    /// <summary>
    /// Igual, pero el texto depende del fallo (por ejemplo, sin conexión frente a sin acceso). <paramref name="errorText"/>
    /// debe devolver un texto apto para el usuario, nunca el detalle de la excepción.
    /// </summary>
    protected async Task RunAsync(Func<Task> action, Func<Exception, string> errorText)
    {
        ErrorMessage = null;
        IsBusy = true;

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ErrorMessage = errorText(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
