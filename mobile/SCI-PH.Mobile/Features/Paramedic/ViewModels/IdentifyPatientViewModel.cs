using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

[QueryProperty(nameof(EmergencyId), "EmergencyId")]
public partial class IdentifyPatientViewModel : BaseViewModel
{
    private readonly IParamedicApiService _api;
    private readonly IFingerprintReaderService _fingerprint;
    private readonly IAppNavigator _navigator;

    public IdentifyPatientViewModel(IParamedicApiService api, IFingerprintReaderService fingerprint, IAppNavigator navigator)
    {
        _api = api;
        _fingerprint = fingerprint;
        _navigator = navigator;
    }

    // PENDIENTE: los códigos de tipo de documento los define el backend (Rol 2).
    public IReadOnlyList<string> DocumentTypes { get; } = new[] { "Cédula", "Pasaporte", "Otro" };

    [ObservableProperty]
    private Guid emergencyId;

    // ---- Resultado ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanIdentify))]
    [NotifyPropertyChangedFor(nameof(ShowFallback))]
    private bool isIdentified;

    [ObservableProperty]
    private string? patientDisplayName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFallback))]
    private bool fingerprintNotFound;

    public bool CanIdentify => !IsIdentified;
    public bool ShowFallback => FingerprintNotFound && !IsIdentified;

    // ---- Formulario de documento ----
    [ObservableProperty]
    private bool showDocumentForm;

    [ObservableProperty]
    private string selectedDocumentType = "Cédula";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitDocumentCommand))]
    private string documentNumber = string.Empty;

    protected override void OnBusyChanged()
    {
        IdentifyByFingerprintCommand.NotifyCanExecuteChanged();
        SubmitDocumentCommand.NotifyCanExecuteChanged();
        ContinueWithoutIdentifyingCommand.NotifyCanExecuteChanged();
    }

    // ---- Comandos ----
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task IdentifyByFingerprintAsync()
    {
        await RunAsync(async () =>
        {
            var read = await _fingerprint.ReadAsync();

            if (!read.Found || read.PatientId is null)
            {
                FingerprintNotFound = true;
                return;
            }

            var emergency = await _api.IdentifyAsync(EmergencyId, new IdentifyEmergencyDto
            {
                Method = IdentificationMethod.Fingerprint,
                PatientId = read.PatientId
            });

            ApplyIdentified(emergency.Patient?.DisplayName ?? read.DisplayName);
        }, "No se pudo completar la identificación. Intenta nuevamente.");
    }

    [RelayCommand]
    private void OpenDocumentForm()
    {
        ErrorMessage = null;
        ShowDocumentForm = true;
    }

    private bool CanSubmitDocument() =>
        !IsBusy && !string.IsNullOrWhiteSpace(DocumentNumber);

    [RelayCommand(CanExecute = nameof(CanSubmitDocument))]
    private async Task SubmitDocumentAsync()
    {
        await RunAsync(async () =>
        {
            var emergency = await _api.IdentifyAsync(EmergencyId, new IdentifyEmergencyDto
            {
                Method = IdentificationMethod.Document,
                Document = new DocumentDto
                {
                    Type = SelectedDocumentType,
                    Number = DocumentNumber.Trim()
                }
            });

            ApplyIdentified(emergency.Patient?.DisplayName);
        }, "No se pudo identificar con el documento. Verifica el número e intenta nuevamente.");
    }

    // La atención ya nace como paciente NN en el dominio: continuar sin identificar
    // no registra nada en el servidor; solo avanza al siguiente paso.
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ContinueWithoutIdentifyingAsync()
    {
        await GoToNextStepAsync();
    }

    [RelayCommand]
    private async Task ContinueAsync()
    {
        await GoToNextStepAsync();
    }

    // ---- Helpers ----
    private bool CanRun() => !IsBusy;

    private void ApplyIdentified(string? displayName)
    {
        PatientDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? "Paciente identificado"
            : displayName;
        IsIdentified = true;
        FingerprintNotFound = false;
        ShowDocumentForm = false;
    }

    private Task GoToNextStepAsync() =>
        _navigator.GoToAsync(
            ParamedicRoutes.VitalSigns,
            new Dictionary<string, object> { ["EmergencyId"] = EmergencyId });
}
