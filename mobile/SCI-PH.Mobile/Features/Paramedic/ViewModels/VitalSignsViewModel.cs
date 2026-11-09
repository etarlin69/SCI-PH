using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Common.ViewModels;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

/// <summary>
/// Captura de signos vitales. Todos los valores son opcionales individualmente (contrato),
/// pero hace falta al menos uno. Los rangos son límites de PLAUSIBILIDAD del dominio
/// (VitalSignLimits) para detectar errores de tipeo; NO son umbrales clínicos ni diagnóstico.
/// </summary>
[QueryProperty(nameof(EmergencyId), "EmergencyId")]
public partial class VitalSignsViewModel : EmergencyWorkflowViewModel
{
    // Límites de plausibilidad (docs: SCI-PH.Domain/Constants/VitalSignLimits.cs)
    private const int HrMin = 0, HrMax = 300;
    private const int RrMin = 0, RrMax = 100;
    private const int SysMin = 0, SysMax = 300;
    private const int DiaMin = 0, DiaMax = 250;
    private const int SpO2Min = 0, SpO2Max = 100;
    private const decimal TempMin = 25m, TempMax = 45m;
    private const int GcsMin = 3, GcsMax = 15;

    private static readonly HashSet<string> InputNames = new()
    {
        nameof(HeartRate), nameof(RespiratoryRate), nameof(SystolicPressure),
        nameof(DiastolicPressure), nameof(OxygenSaturation), nameof(Temperature), nameof(Glasgow)
    };

    private readonly IParamedicApiService _api;
    private readonly IAppNavigator _navigator;

    // Una toma = una clave y una hora, generadas en el primer intento de guardar y conservadas en cada reintento (error de
    // red, respuesta perdida, 400/403/409) para que el servidor no la registre dos veces. Se renuevan solo cuando la toma
    // queda registrada. No son las claves de abrir la emergencia ni de identificar al paciente.
    private Guid? _clientRequestId;
    private DateTime? _recordedAt;

    public VitalSignsViewModel(IParamedicApiService api, IAppNavigator navigator)
        : base(api, navigator)
    {
        _api = api;
        _navigator = navigator;
    }


    // ---- Campos (texto, para validar mientras se escribe) ----
    [ObservableProperty] private string heartRate = string.Empty;
    [ObservableProperty] private string respiratoryRate = string.Empty;
    [ObservableProperty] private string systolicPressure = string.Empty;
    [ObservableProperty] private string diastolicPressure = string.Empty;
    [ObservableProperty] private string oxygenSaturation = string.Empty;
    [ObservableProperty] private string temperature = string.Empty;
    [ObservableProperty] private string glasgow = string.Empty;

    // ---- Errores por campo ----
    public string? HeartRateError => IntError(HeartRate, HrMin, HrMax);
    public string? RespiratoryRateError => IntError(RespiratoryRate, RrMin, RrMax);
    public string? SystolicPressureError => IntError(SystolicPressure, SysMin, SysMax);
    public string? DiastolicPressureError => IntError(DiastolicPressure, DiaMin, DiaMax);
    public string? OxygenSaturationError => IntError(OxygenSaturation, SpO2Min, SpO2Max);
    public string? TemperatureError => DecimalError(Temperature, TempMin, TempMax);
    public string? GlasgowError => IntError(Glasgow, GcsMin, GcsMax);

    public bool HasHeartRateError => HeartRateError is not null;
    public bool HasRespiratoryRateError => RespiratoryRateError is not null;
    public bool HasSystolicPressureError => SystolicPressureError is not null;
    public bool HasDiastolicPressureError => DiastolicPressureError is not null;
    public bool HasOxygenSaturationError => OxygenSaturationError is not null;
    public bool HasTemperatureError => TemperatureError is not null;
    public bool HasGlasgowError => GlasgowError is not null;

    private bool HasAnyValue =>
        !string.IsNullOrWhiteSpace(HeartRate) || !string.IsNullOrWhiteSpace(RespiratoryRate) ||
        !string.IsNullOrWhiteSpace(SystolicPressure) || !string.IsNullOrWhiteSpace(DiastolicPressure) ||
        !string.IsNullOrWhiteSpace(OxygenSaturation) || !string.IsNullOrWhiteSpace(Temperature) ||
        !string.IsNullOrWhiteSpace(Glasgow);

    private bool HasAnyError =>
        HasHeartRateError || HasRespiratoryRateError || HasSystolicPressureError ||
        HasDiastolicPressureError || HasOxygenSaturationError || HasTemperatureError || HasGlasgowError;

    protected override void OnBusyChanged() => SaveCommand.NotifyCanExecuteChanged();

    public ObservableCollection<string> SavedSummaries { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaved))]
    private int savedCount;

    public bool HasSaved => SavedCount > 0;

    // Revalida y reevalúa el botón cuando cambia cualquier campo.
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is null || !InputNames.Contains(e.PropertyName))
            return;

        var field = e.PropertyName;
        OnPropertyChanged(field + "Error");
        OnPropertyChanged("Has" + field + "Error");
        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool CanSave() => !IsBusy && HasAnyValue && !HasAnyError;

    // REAL: una toma de 1 a 7 valores en una sola petición (todo o nada) sobre la MISMA emergencia. Registrada (201, o 200
    // si el servidor ya la tenía con esta clave): se añade a "Tomas guardadas", el formulario se limpia y la pantalla sigue
    // aquí para tomas sucesivas; solo "Continuar" avanza. Si falla, la pantalla queda como estaba.
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            _clientRequestId ??= Guid.NewGuid();
            _recordedAt ??= DateTime.UtcNow;

            var dto = new RegisterVitalsDto
            {
                ClientRequestId = _clientRequestId.Value,
                RecordedAt = _recordedAt.Value,
                HeartRate = ParseInt(HeartRate),
                RespiratoryRate = ParseInt(RespiratoryRate),
                SystolicPressure = ParseInt(SystolicPressure),
                DiastolicPressure = ParseInt(DiastolicPressure),
                OxygenSaturation = ParseInt(OxygenSaturation),
                Temperature = ParseDecimal(Temperature),
                Glasgow = ParseInt(Glasgow)
            };

            await _api.RegisterVitalsAsync(EmergencyId, dto);

            SavedSummaries.Insert(0, BuildSummary(dto));
            SavedCount = SavedSummaries.Count;
            ClearForm();
        }
        catch (MobileApiException ex) when (ex.Error == MobileApiError.Conflict)
        {
            // No se genera otra clave en silencio: podría registrar la toma dos veces.
            ErrorMessage = ParamedicMessages.VitalSignsConflict;
        }
        catch (MobileApiException ex) when (ex.Error is not (MobileApiError.Server or MobileApiError.InvalidResponse))
        {
            ErrorMessage = MobileApiErrorMessages.For(ex);
        }
        catch (Exception)
        {
            ErrorMessage = ParamedicMessages.RegisterVitalSignsFailed;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task ContinueAsync() => GoToProceduresAsync();

    private Task GoToProceduresAsync() =>
        _navigator.GoToAsync(
            ParamedicRoutes.Procedures,
            new Dictionary<string, object> { ["EmergencyId"] = EmergencyId });

    // ---- Helpers ----
    private void ClearForm()
    {
        _clientRequestId = null;
        _recordedAt = null;
        HeartRate = RespiratoryRate = SystolicPressure = DiastolicPressure = string.Empty;
        OxygenSaturation = Temperature = Glasgow = string.Empty;
    }

    private static string BuildSummary(RegisterVitalsDto d)
    {
        var parts = new List<string> { d.RecordedAt.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) };

        if (d.HeartRate is { } hr) parts.Add($"FC {hr}");
        if (d.RespiratoryRate is { } rr) parts.Add($"FR {rr}");
        if (d.SystolicPressure is not null || d.DiastolicPressure is not null)
            parts.Add($"PA {d.SystolicPressure?.ToString() ?? "–"}/{d.DiastolicPressure?.ToString() ?? "–"}");
        if (d.OxygenSaturation is { } sp) parts.Add($"SpO₂ {sp} %");
        if (d.Temperature is { } t) parts.Add($"T {t.ToString("0.0", CultureInfo.CurrentCulture)} °C");
        if (d.Glasgow is { } g) parts.Add($"GCS {g}");

        return string.Join(" · ", parts);
    }

    private static int? ParseInt(string text) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static decimal? ParseDecimal(string text) =>
        decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string? IntError(string text, int min, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = ParseInt(text);
        if (value is null) return "Ingresa un número entero.";
        return value < min || value > max ? $"Valor fuera del rango plausible ({min}–{max})." : null;
    }

    private static string? DecimalError(string text, decimal min, decimal max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = ParseDecimal(text);
        if (value is null) return "Ingresa un número (ej. 36.5).";
        // El servidor guarda hasta 2 decimales y rechaza más, en lugar de redondear.
        if (decimal.Round(value.Value, 2) != value.Value) return "Usa como máximo 2 decimales.";
        return value < min || value > max ? $"Valor fuera del rango plausible ({min}–{max})." : null;
    }
}
