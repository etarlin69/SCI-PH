using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

[QueryProperty(nameof(EmergencyId), "EmergencyId")]
public partial class IdentifyPatientViewModel : ObservableObject
{
    private readonly IParamedicApiService _api;
    private readonly IFingerprintReaderService _fingerprint;

    public IdentifyPatientViewModel(IParamedicApiService api, IFingerprintReaderService fingerprint)
    {
        _api = api;
        _fingerprint = fingerprint;
    }

    // PENDIENTE: los códigos de tipo de documento los define el backend (Rol 2).
    public IReadOnlyList<string> DocumentTypes { get; } = new[] { "Cédula", "Pasaporte", "Otro" };

    [ObservableProperty]
    private Guid emergencyId;

    // ---- Estado general ----
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(IdentifyByFingerprintCommand))]
    [NotifyCanExecuteChangedFor(nameof(SubmitDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueWithoutIdentifyingCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

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

    private async Task GoToNextStepAsync()
    {
        await Shell.Current.GoToAsync(
            ParamedicRoutes.VitalSigns,
            new Dictionary<string, object> { ["EmergencyId"] = EmergencyId });
    }

    private async Task RunAsync(Func<Task> action, string errorText)
    {
        ErrorMessage = null;
        IsBusy = true;

        try
        {
            await action();
        }
        catch (Exception)
        {
            // Mensaje genérico: no exponer detalles internos.
            ErrorMessage = errorText;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
