using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Common.ViewModels;

namespace SCI_PH.Mobile.Features.Patient.ViewModels;

/// <summary>
/// Base de las pantallas con datos del expediente. Shell conserva páginas y ViewModels mientras vive, y la app tiene una
/// sola sesión, así que los datos pertenecen a la cuenta que los pidió:
/// <list type="bullet">
/// <item>si la sesión pasa a otra cuenta o se cierra, se borran de inmediato (también el mensaje de error);</item>
/// <item>una respuesta que llega tarde, de otra cuenta o de una carga anterior, se descarta.</item>
/// </list>
/// Renovar el token de la misma cuenta no borra nada. La suscripción dura lo que la página, que vive lo que el Shell.
/// </summary>
public abstract class PatientRecordViewModel : BaseViewModel
{
    private readonly IClinicalSessionService _session;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private Guid? _owner;
    private int _generation;

    protected PatientRecordViewModel(IClinicalSessionService session)
    {
        _session = session;
        _session.SessionChanged += OnSessionChanged;
    }

    /// <summary>Quita de la pantalla todos los datos del expediente.</summary>
    protected abstract void ClearRecord();

    /// <summary>
    /// Borra lo anterior y carga de nuevo. Si falla, la pantalla queda vacía con el mensaje: nunca hay datos de respaldo.
    /// </summary>
    protected async Task LoadRecordAsync<T>(Func<Task<T>> fetch, Action<T> apply)
    {
        var owner = _session.CurrentUser?.Id;
        var generation = Interlocked.Increment(ref _generation);
        _owner = owner;
        ClearRecord();

        await RunAsync(async () =>
        {
            var record = await fetch();
            if (IsCurrent(owner, generation))
            {
                apply(record);
            }
        }, PatientMessages.For);

        if (!IsCurrent(owner, generation))
        {
            // Lo que falló era de otra cuenta o de una carga ya sustituida: no es un error de esta pantalla.
            ErrorMessage = null;
        }
    }

    private bool IsCurrent(Guid? owner, int generation) =>
        owner is not null && generation == Volatile.Read(ref _generation) && _session.CurrentUser?.Id == owner;

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (_session.CurrentUser?.Id == _owner)
        {
            return;
        }

        _owner = null;
        Interlocked.Increment(ref _generation);

        // La sesión puede cambiar fuera del hilo de la interfaz (por ejemplo, un refresh rechazado).
        if (_ui is null || SynchronizationContext.Current == _ui)
        {
            ClearAll();
        }
        else
        {
            _ui.Post(_ => ClearAll(), null);
        }
    }

    private void ClearAll()
    {
        ClearRecord();
        ErrorMessage = null;
    }
}
