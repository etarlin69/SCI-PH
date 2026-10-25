using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Api;
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

    // Clave de idempotencia de la identificación por documento (no la de la apertura). Se genera al entrar al formulario y
    // se conserva entre reintentos, errores de red, 404 y 409; solo se descarta cuando la identificación se completa.
    private Guid? _documentRequestId;

    public IdentifyPatientViewModel(IParamedicApiService api, IFingerprintReaderService fingerprint, IAppNavigator navigator)
    {
        _api = api;
        _fingerprint = fingerprint;
        _navigator = navigator;
    }

    /// <summary>
    /// Etiqueta visible → tipo del contrato. Solo documentos que demuestran identidad: "Otro" no se ofrece porque el
    /// servidor no identifica con él.
    /// </summary>
    public static IReadOnlyDictionary<string, PatientDocumentType> DocumentTypeByLabel { get; } =
        new Dictionary<string, PatientDocumentType>
        {
            ["Cédula"] = PatientDocumentType.NationalId,
            ["Pasaporte"] = PatientDocumentType.Passport
        };

    public IReadOnlyList<string> DocumentTypes { get; } = DocumentTypeByLabel.Keys.ToList();

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

    /// <summary>
    /// Mientras no esté identificado se ofrecen las tres vías a la vez (huella, documento, NN): ninguna es
    /// requisito de otra, porque en una emergencia la huella puede no ser posible.
    /// </summary>
    public bool CanIdentify => !IsIdentified;

    /// <summary>Solo el aviso de "huella sin coincidencia"; ya no controla qué opciones se muestran.</summary>
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
        _documentRequestId ??= Guid.NewGuid();
        ShowDocumentForm = true;
    }

    private bool CanSubmitDocument() =>
        !IsBusy && !string.IsNullOrWhiteSpace(DocumentNumber);

    // REAL: el servidor busca el documento y vincula al paciente la MISMA emergencia. El número va tal como se escribió
    // (la normalización es del servidor). Sin coincidencia, la emergencia sigue NN: se puede corregir, reintentar o
    // continuar sin identificar.
    [RelayCommand(CanExecute = nameof(CanSubmitDocument))]
    private async Task SubmitDocumentAsync()
    {
        if (!DocumentTypeByLabel.TryGetValue(SelectedDocumentType ?? string.Empty, out var documentType))
        {
            ErrorMessage = ParamedicMessages.DocumentTypeRequired;
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            _documentRequestId ??= Guid.NewGuid();
            var emergency = await _api.IdentifyByDocumentAsync(EmergencyId, new IdentifyByDocumentDto
            {
                ClientRequestId = _documentRequestId.Value,
                DocumentType = documentType,
                DocumentNumber = DocumentNumber
            });

            if (emergency is null)
            {
                ErrorMessage = ParamedicMessages.PatientNotFoundByDocument;
                return;
            }

            _documentRequestId = null;
            ApplyIdentified(emergency.Patient?.DisplayName);
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.Conflict)
        {
            // No se genera otra clave en silencio: podría identificar otra vez la emergencia.
            ErrorMessage = ParamedicMessages.IdentificationConflict;
        }
        catch (MobileApiException ex) when (ex.Error is not (MobileApiError.Server or MobileApiError.InvalidResponse))
        {
            ErrorMessage = MobileApiErrorMessages.For(ex);
        }
        catch (Exception)
        {
            ErrorMessage = ParamedicMessages.IdentifyByDocumentFailed;
        }
        finally
        {
            IsBusy = false;
        }
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
