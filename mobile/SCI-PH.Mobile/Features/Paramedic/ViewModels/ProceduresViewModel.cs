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

[QueryProperty(nameof(EmergencyId), "EmergencyId")]
public partial class ProceduresViewModel : BaseViewModel
{
    /// <summary>Límite del resultado en el servidor (ClinicalProcedure.Outcome).</summary>
    public const int OutcomeMaxLength = 4000;

    private readonly IParamedicApiService _api;
    private readonly IParamedicCatalogService _catalog;
    private readonly IAppNavigator _navigator;

    // Un procedimiento = una clave y una hora, generadas en el primer intento de guardar y conservadas en cada reintento
    // (error de red, respuesta perdida, 400/403/404/409) para que el servidor no lo registre dos veces. Se renuevan solo
    // cuando el procedimiento queda registrado. No son las claves de la emergencia ni de los signos vitales.
    private Guid? _clientRequestId;
    private DateTime? _performedAt;

    public ProceduresViewModel(IParamedicApiService api, IParamedicCatalogService catalog, IAppNavigator navigator)
    {
        _api = api;
        _catalog = catalog;
        _navigator = navigator;
    }

    [ObservableProperty]
    private Guid emergencyId;

    public ObservableCollection<CatalogItemDto> CatalogItems { get; } = new();
    public ObservableCollection<string> SavedSummaries { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(SelectedProcedureDescription), nameof(HasSelectedProcedureDescription))]
    private CatalogItemDto? selectedProcedure;

    /// <summary>Descripción del procedimiento elegido, tal como la define el catálogo del servidor.</summary>
    public string? SelectedProcedureDescription => SelectedProcedure?.Description;

    public bool HasSelectedProcedureDescription => !string.IsNullOrWhiteSpace(SelectedProcedureDescription);

    /// <summary>Resultado del procedimiento (opcional).</summary>
    [ObservableProperty]
    private string outcome = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CatalogLoadFailed))]
    private bool catalogFailed;

    public bool CatalogLoadFailed => CatalogFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaved))]
    private int savedCount;

    public bool HasSaved => SavedCount > 0;

    protected override void OnBusyChanged() => SaveCommand.NotifyCanExecuteChanged();

    // REAL: el catálogo fijo del servidor (GET /api/mobile/catalogs/procedures).
    [RelayCommand]
    private async Task LoadCatalogAsync()
    {
        if (CatalogItems.Count > 0) return;

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            foreach (var item in await _catalog.GetProceduresAsync()) CatalogItems.Add(item);
            CatalogFailed = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex is MobileApiException api && api.Error is not (MobileApiError.Server or MobileApiError.InvalidResponse)
                ? MobileApiErrorMessages.For(api)
                : ParamedicMessages.ProceduresCatalogFailed;
            CatalogFailed = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSave() => !IsBusy && SelectedProcedure is not null && (Outcome?.Trim().Length ?? 0) <= OutcomeMaxLength;

    // REAL: un procedimiento ya realizado sobre la MISMA emergencia. Registrado (201, o 200 si el servidor ya lo tenía con
    // esta clave) → Medicamentos. Si falla, la pantalla queda como estaba.
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (SelectedProcedure is not { } procedure) return;

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            _clientRequestId ??= Guid.NewGuid();
            _performedAt ??= DateTime.UtcNow;

            var dto = new RegisterProcedureDto
            {
                ClientRequestId = _clientRequestId.Value,
                PerformedAt = _performedAt.Value,
                ProcedureCode = procedure.Code,
                Outcome = string.IsNullOrWhiteSpace(Outcome) ? null : Outcome.Trim()
            };

            await _api.RegisterProcedureAsync(EmergencyId, dto);

            var time = dto.PerformedAt.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
            var summary = $"{time} · {procedure.Name}";
            if (dto.Outcome is not null) summary += $" · {dto.Outcome}";
            SavedSummaries.Insert(0, summary);
            SavedCount = SavedSummaries.Count;
            ClearForm();
            await GoToMedicationsAsync();
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.Conflict)
        {
            // No se genera otra clave en silencio: podría registrar el procedimiento dos veces.
            ErrorMessage = ParamedicMessages.ProcedureConflict;
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
            ErrorMessage = ParamedicMessages.RegisterProcedureFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task ContinueAsync() => GoToMedicationsAsync();

    partial void OnOutcomeChanged(string value) => SaveCommand.NotifyCanExecuteChanged();

    private Task GoToMedicationsAsync() =>
        _navigator.GoToAsync(
            ParamedicRoutes.Medications,
            new Dictionary<string, object> { ["EmergencyId"] = EmergencyId });

    private void ClearForm()
    {
        _clientRequestId = null;
        _performedAt = null;
        SelectedProcedure = null;
        Outcome = string.Empty;
    }
}
