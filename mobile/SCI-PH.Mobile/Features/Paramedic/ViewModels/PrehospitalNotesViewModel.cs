using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

/// <summary>
/// Observaciones REALES del paramédico (notas de tipo Prehospital). Las notas no se editan: una corrección es otra nota.
/// La hora es automática y no editable.
/// </summary>
[QueryProperty(nameof(EmergencyId), "EmergencyId")]
public partial class PrehospitalNotesViewModel : BaseViewModel
{
    /// <summary>Límite de UX en el teléfono; el servidor admite hasta 8000 caracteres.</summary>
    public const int ContentMaxLength = 4000;

    private readonly IParamedicApiService _api;
    private readonly IAppNavigator _navigator;

    // Una observación = una clave y una hora, generadas en el primer intento de guardar y conservadas en cada reintento
    // (error de red, respuesta perdida, 400/403/404/409) para que el servidor no la registre dos veces. Se renuevan solo
    // cuando la observación queda registrada.
    private Guid? _clientRequestId;
    private DateTime? _occurredAt;

    public PrehospitalNotesViewModel(IParamedicApiService api, IAppNavigator navigator)
    {
        _api = api;
        _navigator = navigator;
    }

    [ObservableProperty]
    private Guid emergencyId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContentCounter))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string content = string.Empty;

    /// <summary>Hora de la observación en curso (solo lectura).</summary>
    [ObservableProperty]
    private string occurredAtText = ParamedicMessages.NoteTimePending;

    /// <summary>Confirmación de la última observación registrada.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuccess))]
    private string? successMessage;

    public bool HasSuccess => !string.IsNullOrWhiteSpace(SuccessMessage);

    public string ContentCounter => $"{Content?.Length ?? 0}/{ContentMaxLength}";

    public ObservableCollection<string> SavedSummaries { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaved))]
    private int savedCount;

    public bool HasSaved => SavedCount > 0;

    protected override void OnBusyChanged() => SaveCommand.NotifyCanExecuteChanged();

    private bool CanSave() =>
        !IsBusy && !string.IsNullOrWhiteSpace(Content) && Content.Trim().Length <= ContentMaxLength;

    // REAL: una observación sobre la MISMA emergencia. Registrada (201, o 200 si el servidor ya la tenía con esta clave):
    // confirmación, editor limpio y se queda en esta pantalla para otra observación. Si falla, el texto queda como estaba.
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        SuccessMessage = null;
        IsBusy = true;
        try
        {
            _clientRequestId ??= Guid.NewGuid();
            if (_occurredAt is null)
            {
                _occurredAt = DateTime.UtcNow;
                OccurredAtText = TimeText(_occurredAt.Value);
            }

            var dto = new RegisterNoteDto
            {
                ClientRequestId = _clientRequestId.Value,
                OccurredAt = _occurredAt.Value,
                Content = Content.Trim()
            };

            await _api.RegisterNoteAsync(EmergencyId, dto);

            SavedSummaries.Insert(0, $"{TimeText(dto.OccurredAt)} · {dto.Content}");
            SavedCount = SavedSummaries.Count;
            ClearForm();
            SuccessMessage = ParamedicMessages.NoteRecorded;
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.Conflict)
        {
            // No se genera otra clave en silencio: podría registrar la misma observación dos veces.
            ErrorMessage = ParamedicMessages.NoteConflict;
        }
        catch (MobileApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            ErrorMessage = ParamedicMessages.EmergencyNotFound;
        }
        catch (MobileApiException ex) when (ex.Error is not (MobileApiError.Server or MobileApiError.InvalidResponse))
        {
            ErrorMessage = MobileApiErrorMessages.For(ex);
        }
        catch (Exception)
        {
            ErrorMessage = ParamedicMessages.RegisterNoteFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Siguiente paso del flujo: el traslado de la misma emergencia.
    [RelayCommand]
    private Task ContinueAsync() =>
        _navigator.GoToAsync(
            ParamedicRoutes.Transfer,
            new Dictionary<string, object> { ["EmergencyId"] = EmergencyId });

    private void ClearForm()
    {
        _clientRequestId = null;
        _occurredAt = null;
        OccurredAtText = ParamedicMessages.NoteTimePending;
        Content = string.Empty;
    }

    private static string TimeText(DateTime utc) => utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
}
