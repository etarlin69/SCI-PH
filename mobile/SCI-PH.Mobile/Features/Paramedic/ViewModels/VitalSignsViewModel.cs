using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.ViewModels;

/// <summary>
/// Captura de signos vitales. Todos los valores son opcionales individualmente (contrato),
/// pero hace falta al menos uno. Los rangos son límites de PLAUSIBILIDAD del dominio
/// (VitalSignLimits) para detectar errores de tipeo; NO son umbrales clínicos ni diagnóstico.
/// </summary>
[QueryProperty(nameof(EmergencyId), "EmergencyId")]
public partial class VitalSignsViewModel : ObservableObject
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

    // Se conservan entre reintentos para que un reenvío no duplique el registro.
    private Guid? _clientRequestId;
    private DateTime? _recordedAt;

    public VitalSignsViewModel(IParamedicApiService api)
    {
        _api = api;
    }

    [ObservableProperty]
    private Guid emergencyId;

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

    // ---- Estado ----
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

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
        catch (Exception)
        {
            // Mensaje genérico; los datos y el ClientRequestId se conservan para reintentar.
            ErrorMessage = "No se pudieron guardar los signos vitales. Intenta nuevamente.";
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
            ParamedicRoutes.Procedures,
            new Dictionary<string, object> { ["EmergencyId"] = EmergencyId });
    }

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
        return value < min || value > max ? $"Valor fuera del rango plausible ({min}–{max})." : null;
    }
}
