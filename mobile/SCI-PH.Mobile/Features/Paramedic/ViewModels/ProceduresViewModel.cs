using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

[QueryProperty(nameof(EmergencyId), "EmergencyId")]
public partial class ProceduresViewModel : BaseViewModel
{
    private readonly IParamedicApiService _api;
    private readonly IParamedicCatalogService _catalog;
    private readonly IAppNavigator _navigator;

    // Se conservan entre reintentos para que un reenvío no duplique el registro.
    private Guid? _clientRequestId;
    private DateTime? _recordedAt;

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
    private CatalogItemDto? selectedProcedure;

    [ObservableProperty]
    private string notes = string.Empty;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CatalogLoadFailed))]
    private bool catalogFailed;

    public bool CatalogLoadFailed => CatalogFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaved))]
    private int savedCount;

    public bool HasSaved => SavedCount > 0;

    protected override void OnBusyChanged() => SaveCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private async Task LoadCatalogAsync()
    {
        if (CatalogItems.Count > 0) return;

        await RunAsync(async () =>
        {
            var items = await _catalog.GetProceduresAsync();
            foreach (var item in items) CatalogItems.Add(item);
        }, "No se pudo cargar el catálogo de procedimientos.");

        CatalogFailed = HasError;
    }

    private bool CanSave() => !IsBusy && SelectedProcedure is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (SelectedProcedure is null) return;

        // Si falla, los datos y el ClientRequestId se conservan para reintentar.
        await RunAsync(async () =>
        {
            _clientRequestId ??= Guid.NewGuid();
            _recordedAt ??= DateTime.UtcNow;

            var dto = new RegisterProcedureDto
            {
                ClientRequestId = _clientRequestId.Value,
                RecordedAt = _recordedAt.Value,
                ProcedureCode = SelectedProcedure.Code,
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim()
            };

            await _api.RegisterProcedureAsync(EmergencyId, dto);

            var time = dto.RecordedAt.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
            var summary = $"{time} · {SelectedProcedure.Name}";
            if (dto.Notes is not null) summary += $" · {dto.Notes}";
            SavedSummaries.Insert(0, summary);
            SavedCount = SavedSummaries.Count;

            _clientRequestId = null;
            _recordedAt = null;
            SelectedProcedure = null;
            Notes = string.Empty;
        }, "No se pudo guardar el procedimiento. Intenta nuevamente.");
    }

    [RelayCommand]
    private async Task ContinueAsync()
    {
        await _navigator.GoToAsync(
            ParamedicRoutes.Medications,
            new Dictionary<string, object> { ["EmergencyId"] = EmergencyId });
    }
}
