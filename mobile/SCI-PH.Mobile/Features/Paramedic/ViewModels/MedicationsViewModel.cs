using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Common.Dialogs;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

/// <summary>
/// Registro de medicamentos YA ADMINISTRADOS. SCI-PH no prescribe ni sugiere dosis.
/// </summary>
[QueryProperty(nameof(EmergencyId), "EmergencyId")]
public partial class MedicationsViewModel : BaseViewModel
{
    private readonly IParamedicApiService _api;
    private readonly IParamedicCatalogService _catalog;
    private readonly IAppDialogService _dialogs;

    // Se conservan entre reintentos para que un reenvío no duplique el registro.
    private Guid? _clientRequestId;
    private DateTime? _recordedAt;

    public MedicationsViewModel(IParamedicApiService api, IParamedicCatalogService catalog, IAppDialogService dialogs)
    {
        _api = api;
        _catalog = catalog;
        _dialogs = dialogs;
    }

    // PENDIENTE: el contrato define "route" como texto; confirmar valores válidos con Rol 2.
    public IReadOnlyList<string> Routes { get; } = new[]
    {
        "Intravenosa", "Intramuscular", "Subcutánea", "Oral",
        "Sublingual", "Inhalatoria", "Intranasal", "Otra"
    };

    [ObservableProperty]
    private Guid emergencyId;

    public ObservableCollection<CatalogItemDto> CatalogItems { get; } = new();
    public ObservableCollection<string> SavedSummaries { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private CatalogItemDto? selectedMedication;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string dose = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? selectedRoute;

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
            var items = await _catalog.GetMedicationsAsync();
            foreach (var item in items) CatalogItems.Add(item);
        }, "No se pudo cargar el catálogo de medicamentos.");

        CatalogFailed = HasError;
    }

    private bool CanSave() =>
        !IsBusy &&
        SelectedMedication is not null &&
        !string.IsNullOrWhiteSpace(Dose) &&
        !string.IsNullOrWhiteSpace(SelectedRoute);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (SelectedMedication is null || SelectedRoute is null) return;

        // Si falla, los datos y el ClientRequestId se conservan para reintentar.
        await RunAsync(async () =>
        {
            _clientRequestId ??= Guid.NewGuid();
            _recordedAt ??= DateTime.UtcNow;

            var dto = new RegisterMedicationDto
            {
                ClientRequestId = _clientRequestId.Value,
                RecordedAt = _recordedAt.Value,
                MedicationCode = SelectedMedication.Code,
                Dose = Dose.Trim(),
                Route = SelectedRoute,
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim()
            };

            await _api.RegisterMedicationAsync(EmergencyId, dto);

            var time = dto.RecordedAt.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
            var summary = $"{time} · {SelectedMedication.Name} · {dto.Dose} · {dto.Route}";
            if (dto.Notes is not null) summary += $" · {dto.Notes}";
            SavedSummaries.Insert(0, summary);
            SavedCount = SavedSummaries.Count;

            _clientRequestId = null;
            _recordedAt = null;
            SelectedMedication = null;
            SelectedRoute = null;
            Dose = string.Empty;
            Notes = string.Empty;
        }, "No se pudo guardar el medicamento. Intenta nuevamente.");
    }

    [RelayCommand]
    private async Task ContinueAsync()
    {
        // TODO: navegar a Transferencia (ParamedicRoutes.Transfer).
        await _dialogs.ShowAsync(
            "Medicamentos",
            $"Administraciones registradas: {SavedCount}\nSiguiente paso: transferencia (pendiente).",
            "OK");
    }
}
