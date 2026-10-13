using CommunityToolkit.Mvvm.ComponentModel;

namespace SCI_PH.PatientPrototype.Features.Patient.ViewModels;

/// <summary>Base de los ViewModels del paciente: estado de carga y un mensaje de error apto para el paciente.</summary>
public abstract partial class PatientViewModel : ObservableObject
{
    public const string GenericErrorMessage = "No se pudo completar la operación. Inténtalo de nuevo.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool IsNotBusy => !IsBusy;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Ejecuta una operación mostrando la carga; un fallo deja el mensaje genérico, nunca el de la excepción.</summary>
    protected async Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await operation(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            ErrorMessage = GenericErrorMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
