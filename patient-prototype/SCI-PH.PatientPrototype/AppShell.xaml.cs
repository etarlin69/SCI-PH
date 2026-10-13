using System.Windows.Input;
using SCI_PH.PatientPrototype.Features.Patient;
using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

namespace SCI_PH.PatientPrototype;

/// <summary>
/// Shell de la app. El menú solo se habilita con sesión iniciada; al cerrar sesión se borra la sesión y se vuelve al
/// acceso por QR.
/// </summary>
public partial class AppShell : Shell
{
    private readonly IPatientSessionService _session;

    public AppShell(IPatientSessionService session)
    {
        _session = session;
        LogoutCommand = new Command(async () => await LogoutAsync());

        InitializeComponent();
        PatientServiceCollectionExtensions.RegisterPatientRoutes();

        BindingContext = this;
        _session.SessionChanged += (_, _) => MainThread.BeginInvokeOnMainThread(ApplySession);
        ApplySession();
    }

    public string PatientDisplayName => _session.CurrentSession?.PatientName ?? string.Empty;

    public ICommand LogoutCommand { get; }

    private void ApplySession()
    {
        FlyoutBehavior = _session.IsAuthenticated ? FlyoutBehavior.Flyout : FlyoutBehavior.Disabled;
        OnPropertyChanged(nameof(PatientDisplayName));
    }

    private async Task LogoutAsync()
    {
        FlyoutIsPresented = false;
        await _session.ClearSessionAsync();
        await GoToAsync(PatientRoutes.Root(PatientRoutes.QrAccess));
    }
}
