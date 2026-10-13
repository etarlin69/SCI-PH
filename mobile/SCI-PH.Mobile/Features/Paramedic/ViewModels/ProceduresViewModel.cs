using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

[QueryProperty(nameof(EmergencyId), "EmergencyId")]
public partial class ProceduresViewModel : ObservableObject
{
    private readonly IParamedicApiService _api;
    private readonly IParamedicCatalogService _catalog;

    // Se conservan entre reintentos para que un reenvío no duplique el registro.
    private Guid? _clientRequestId;
    private DateTime? _recordedAt;

    public ProceduresViewModel(IParamedicApiService api, IParamedicCatalogService catalog)
    {
        _api = api;
        _catalog = catalog;
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
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CatalogLoadFailed))]
    private bool catalogFailed;

    public bool CatalogLoadFailed => CatalogFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaved))]
    private int savedCount;

    public bool HasSaved => SavedCount > 0;

    [RelayCommand]
    private async Task LoadCatalogAsync()
    {
        if (CatalogItems.Count > 0) return;

        CatalogFailed = false;
        IsBusy = true;

        try
        {
            var items = await _catalog.GetProceduresAsync();
            foreach (var item in items) CatalogItems.Add(item);
        }
        catch (Exception)
        {
            CatalogFailed = true;
            ErrorMessage = "No se pudo cargar el catálogo de procedimientos.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSave() => !IsBusy && SelectedProcedure is not null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (SelectedProcedure is null) return;

        ErrorMessage = null;
        IsBusy = true;

        try
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
        }
        catch (Exception)
        {
            // Mensaje genérico; los datos y el ClientRequestId se conservan para reintentar.
            ErrorMessage = "No se pudo guardar el procedimiento. Intenta nuevamente.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ContinueAsync()
    {
        await Shell.Current.GoToAsync(
            ParamedicRoutes.Medications,
            new Dictionary<string, object> { ["EmergencyId"] = EmergencyId });
    }
}
