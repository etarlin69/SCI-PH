using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Patient.ViewModels;

/// <summary>
/// "Mi QR". El contenido solo existe en esta memoria mientras sea válido y la sesión sea de esa cuenta
/// (<see cref="PatientRecordViewModel"/>): al cerrar la sesión o cambiar de cuenta se borra, una respuesta tardía se descarta,
/// y una app reabierta pide uno nuevo. Sin conexión no hay QR: nunca se muestra uno vencido.
/// </summary>
public sealed partial class MyQrViewModel : PatientRecordViewModel
{
    private readonly IPatientQrService _qr;
    private readonly TimeProvider _time;
    private DateTimeOffset? _deadline;

    public MyQrViewModel(IPatientQrService qr, IClinicalSessionService session, TimeProvider time)
        : base(session)
    {
        _qr = qr;
        _time = time;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQr))]
    private string? payload;

    [ObservableProperty]
    private string? expiresIn;

    /// <summary>El QR anterior venció y se está pidiendo otro.</summary>
    [ObservableProperty]
    private bool isRenewing;

    public bool HasQr => Payload is not null;

    /// <summary>Al entrar en la pantalla: sigue el QR vigente en esta sesión; si no lo hay, pide uno nuevo.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (HasQr && !IsExpired())
        {
            UpdateExpiresIn();
            return;
        }

        await IssueAsync();
    }

    /// <summary>Volver a intentarlo tras un error.</summary>
    [RelayCommand]
    private Task RetryAsync() => IssueAsync();

    /// <summary>
    /// Cada segundo con la pantalla visible: actualiza la cuenta atrás y, al vencer, quita el QR y pide otro. Si esa petición
    /// falla, queda el error y no se reintenta solo: el reintento es del paciente.
    /// </summary>
    public async Task TickAsync()
    {
        if (!HasQr)
        {
            return;
        }

        if (!IsExpired())
        {
            UpdateExpiresIn();
            return;
        }

        // El QR vencido se borra al empezar, antes de esperar respuesta.
        var issuing = IssueAsync();
        IsRenewing = !issuing.IsCompleted;
        await issuing;
        IsRenewing = false;
    }

    protected override void ClearRecord()
    {
        Payload = null;
        ExpiresIn = null;
        IsRenewing = false;
        _deadline = null;
    }

    private Task IssueAsync() => LoadRecordAsync(() => _qr.IssueAsync(), issued =>
    {
        _deadline = _time.GetUtcNow() + issued.Lifetime;
        Payload = issued.Payload;
        UpdateExpiresIn();
    });

    private bool IsExpired() => _deadline is not { } deadline || _time.GetUtcNow() >= deadline;

    private void UpdateExpiresIn()
    {
        var remaining = _deadline - _time.GetUtcNow();
        // Los segundos se redondean hacia arriba: nunca muestra 00:00 con el código aún válido.
        var seconds = remaining is { } value ? (int)Math.Ceiling(value.TotalSeconds) : 0;
        ExpiresIn = $"Caduca en {seconds / 60:00}:{seconds % 60:00}";
    }
}
