using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Dialogs;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

/// <summary>Vía de administración con su etiqueta en español; al servidor viaja el nombre del enum.</summary>
public sealed record MedicationRouteOption(MedicationRoute Value, string Label);

/// <summary>
/// Registro REAL de medicamentos YA ADMINISTRADOS (sin prescripción). SCI-PH no prescribe ni sugiere dosis: los límites
/// de dosis y unidad son los del servidor, no reglas clínicas.
/// </summary>
[QueryProperty(nameof(EmergencyId), "EmergencyId")]
public partial class MedicationsViewModel : BaseViewModel
{
    /// <summary>Límites del servidor (MedicationAdministration y su columna numeric(12,3)).</summary>
    public const int MaxDoseDecimals = 3;
    public const decimal DoseUpperBound = 1_000_000_000m;
    public const int DoseUnitMaxLength = 24;
    public const int NotesMaxLength = 4000;

    /// <summary>Opción de la lista de unidades que abre el texto libre.</summary>
    public const string OtherDoseUnit = "Otra";

    private static readonly HashSet<string> InputNames = new()
    {
        nameof(SelectedMedication), nameof(Dose), nameof(SelectedDoseUnit), nameof(CustomDoseUnit), nameof(SelectedRoute), nameof(Notes)
    };

    private readonly IParamedicApiService _api;
    private readonly IParamedicCatalogService _catalog;
    private readonly IAppDialogService _dialogs;

    // Una dosis = una clave y una hora, generadas en el primer intento de guardar y conservadas en cada reintento (error de
    // red, respuesta perdida, 400/403/404/409) para que el servidor no registre la misma dosis dos veces. Se renuevan solo
    // cuando la dosis queda registrada.
    private Guid? _clientRequestId;
    private DateTime? _administeredAt;

    public MedicationsViewModel(IParamedicApiService api, IParamedicCatalogService catalog, IAppDialogService dialogs)
    {
        _api = api;
        _catalog = catalog;
        _dialogs = dialogs;
    }

    /// <summary>Los 13 valores de MedicationRoute del servidor.</summary>
    public IReadOnlyList<MedicationRouteOption> Routes { get; } =
    [
        new(MedicationRoute.Oral, "Oral"),
        new(MedicationRoute.Intravenous, "Intravenosa"),
        new(MedicationRoute.Intramuscular, "Intramuscular"),
        new(MedicationRoute.Subcutaneous, "Subcutánea"),
        new(MedicationRoute.Inhalation, "Inhalatoria"),
        new(MedicationRoute.Topical, "Tópica"),
        new(MedicationRoute.Sublingual, "Sublingual"),
        new(MedicationRoute.Rectal, "Rectal"),
        new(MedicationRoute.Nasal, "Nasal"),
        new(MedicationRoute.Ophthalmic, "Oftálmica"),
        new(MedicationRoute.Otic, "Ótica"),
        new(MedicationRoute.Transdermal, "Transdérmica"),
        new(MedicationRoute.Other, "Otra")
    ];

    /// <summary>Ayuda de UI, no autoridad clínica: el servidor acepta cualquier unidad de 1 a 24 caracteres.</summary>
    public IReadOnlyList<string> DoseUnits { get; } = ["mg", "mcg", "g", "mL", "UI", "gotas", "inhalaciones", OtherDoseUnit];

    [ObservableProperty]
    private Guid emergencyId;

    public ObservableCollection<MedicationCatalogItemDto> CatalogItems { get; } = new();
    public ObservableCollection<string> SavedSummaries { get; } = new();

    [ObservableProperty]
    private MedicationCatalogItemDto? selectedMedication;

    [ObservableProperty]
    private string dose = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomDoseUnit))]
    private string? selectedDoseUnit;

    [ObservableProperty]
    private string customDoseUnit = string.Empty;

    [ObservableProperty]
    private MedicationRouteOption? selectedRoute;

    [ObservableProperty]
    private string notes = string.Empty;

    /// <summary>Confirmación de la última dosis registrada.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuccess))]
    private string? successMessage;

    public bool HasSuccess => !string.IsNullOrWhiteSpace(SuccessMessage);

    public bool IsCustomDoseUnit => SelectedDoseUnit == OtherDoseUnit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CatalogLoadFailed))]
    private bool catalogFailed;

    public bool CatalogLoadFailed => CatalogFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaved))]
    private int savedCount;

    public bool HasSaved => SavedCount > 0;

    // ---- Validación local (UX; el servidor es la autoridad) ----
    public string? DoseError => string.IsNullOrWhiteSpace(Dose) ? null : DoseErrorFor(Dose);
    public bool HasDoseError => DoseError is not null;

    public string? DoseUnitError =>
        SelectedDoseUnit is null ? null
        : !IsCustomDoseUnit ? null
        : string.IsNullOrWhiteSpace(CustomDoseUnit) ? null
        : CustomDoseUnit.Trim().Length > DoseUnitMaxLength ? $"Máximo {DoseUnitMaxLength} caracteres."
        : null;

    public bool HasDoseUnitError => DoseUnitError is not null;

    /// <summary>La unidad que se enviará: la elegida o, con "Otra", la escrita (recortada).</summary>
    public string? EffectiveDoseUnit
    {
        get
        {
            var unit = (IsCustomDoseUnit ? CustomDoseUnit : SelectedDoseUnit)?.Trim();
            return string.IsNullOrEmpty(unit) || unit.Length > DoseUnitMaxLength ? null : unit;
        }
    }

    protected override void OnBusyChanged() => SaveCommand.NotifyCanExecuteChanged();

    // Revalida y reevalúa el botón cuando cambia cualquier campo.
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is null || !InputNames.Contains(e.PropertyName))
            return;

        OnPropertyChanged(nameof(DoseError));
        OnPropertyChanged(nameof(HasDoseError));
        OnPropertyChanged(nameof(DoseUnitError));
        OnPropertyChanged(nameof(HasDoseUnitError));
        OnPropertyChanged(nameof(EffectiveDoseUnit));
        SaveCommand.NotifyCanExecuteChanged();
    }

    // La vía por defecto del catálogo se preselecciona; el paramédico puede cambiarla.
    partial void OnSelectedMedicationChanged(MedicationCatalogItemDto? value)
    {
        if (value?.DefaultRoute is { } route)
        {
            SelectedRoute = Routes.Single(r => r.Value == route);
        }
    }

    // REAL: los medicamentos activos del servidor (GET /api/mobile/catalogs/medications).
    [RelayCommand]
    private async Task LoadCatalogAsync()
    {
        if (CatalogItems.Count > 0) return;

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            foreach (var item in await _catalog.GetMedicationsAsync()) CatalogItems.Add(item);
            CatalogFailed = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex is MobileApiException api && api.Error is not (MobileApiError.Server or MobileApiError.InvalidResponse)
                ? MobileApiErrorMessages.For(api)
                : ParamedicMessages.MedicationsCatalogFailed;
            CatalogFailed = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSave() =>
        !IsBusy &&
        SelectedMedication is not null &&
        ParseDose(Dose) is not null && DoseErrorFor(Dose) is null &&
        EffectiveDoseUnit is not null &&
        SelectedRoute is not null &&
        (Notes?.Trim().Length ?? 0) <= NotesMaxLength;

    // REAL: una dosis ya administrada sobre la MISMA emergencia, sin prescripción. Registrada (201, o 200 si el servidor ya
    // la tenía con esta clave): confirmación, formulario limpio y se queda en esta pantalla para la próxima dosis. Si falla,
    // la pantalla queda como estaba.
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (SelectedMedication is not { } medication || SelectedRoute is not { } route || ParseDose(Dose) is not { } dose
            || EffectiveDoseUnit is not { } unit)
            return;

        ErrorMessage = null;
        SuccessMessage = null;
        IsBusy = true;
        try
        {
            _clientRequestId ??= Guid.NewGuid();
            _administeredAt ??= DateTime.UtcNow;

            var dto = new RegisterMedicationDto
            {
                ClientRequestId = _clientRequestId.Value,
                AdministeredAt = _administeredAt.Value,
                MedicationId = medication.Id,
                Dose = dose,
                DoseUnit = unit,
                Route = route.Value,
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim()
            };

            await _api.RegisterMedicationAsync(EmergencyId, dto);

            var time = dto.AdministeredAt.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
            var summary = $"{time} · {medication.DisplayName} · {dto.Dose.ToString(CultureInfo.CurrentCulture)} {dto.DoseUnit} · {route.Label}";
            if (dto.Notes is not null) summary += $" · {dto.Notes}";
            SavedSummaries.Insert(0, summary);
            SavedCount = SavedSummaries.Count;
            ClearForm();
            SuccessMessage = ParamedicMessages.MedicationRecorded;
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.Conflict)
        {
            // No se genera otra clave en silencio: podría registrar la misma dosis dos veces.
            ErrorMessage = ParamedicMessages.MedicationAdministrationConflict;
        }
        catch (MobileApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            ErrorMessage = ParamedicMessages.MedicationOrEmergencyNotFound;
        }
        catch (MobileApiException ex) when (ex.Error is not (MobileApiError.Server or MobileApiError.InvalidResponse))
        {
            ErrorMessage = MobileApiErrorMessages.For(ex);
        }
        catch (Exception)
        {
            ErrorMessage = ParamedicMessages.RegisterMedicationFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Transferencia aún no existe: se informa con un diálogo en lugar de navegar.
    [RelayCommand]
    private async Task ContinueAsync()
    {
        // TODO: navegar a Transferencia (ParamedicRoutes.Transfer).
        await _dialogs.ShowAsync(
            "Medicamentos",
            $"Administraciones registradas: {SavedCount}\nSiguiente paso: transferencia (pendiente).",
            "OK");
    }

    // ---- Helpers ----
    private void ClearForm()
    {
        _clientRequestId = null;
        _administeredAt = null;
        SelectedMedication = null;
        SelectedRoute = null;
        SelectedDoseUnit = null;
        Dose = CustomDoseUnit = Notes = string.Empty;
    }

    // Acepta coma o punto como separador decimal; nunca separadores de miles.
    public static decimal? ParseDose(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var normalized = text.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string? DoseErrorFor(string text)
    {
        if (ParseDose(text) is not { } value) return "Ingresa un número (ej. 500 o 2,5).";
        if (value <= 0) return "La dosis debe ser mayor que 0.";
        if (decimal.Round(value, MaxDoseDecimals) != value) return $"Usa como máximo {MaxDoseDecimals} decimales.";
        return value >= DoseUpperBound ? "La dosis es demasiado grande." : null;
    }
}
