using System.Globalization;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Features.Patient.Models.Dtos;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Patient.Services;

/// <summary>
/// Expediente propio REAL del paciente autenticado: solo rutas <c>api/mobile/patient/me/...</c>, sin ningún id de
/// paciente (el servidor lo deduce de la sesión, ADR-015). Las peticiones van con <see cref="IMobileApiClient"/>, que
/// añade el Bearer y renueva la sesión ante un 401. Los enums llegan como texto y se traducen aquí; un valor nuevo se
/// muestra tal cual. Los fallos llegan como <see cref="MobileApiException"/>; nunca se devuelven datos simulados.
/// </summary>
public sealed class HttpPatientDataService : IPatientDataService
{
    private const string Me = "api/mobile/patient/me";
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    private readonly IMobileApiClient _client;

    public HttpPatientDataService(IMobileApiClient client) => _client = client;

    public async Task<PatientSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var summary = await _client.GetAsync<SummaryResponse>($"{Me}/summary", cancellationToken);
        return new PatientSummaryDto(
            summary.DisplayName,
            summary.ResultCount,
            summary.EncounterCount,
            summary.LatestEncounter is { } encounter ? ToEpisode(encounter) : null,
            summary.LatestResult is { } result ? ToResult(result) : null);
    }

    public async Task<ClinicalHistoryDto> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        var allergies = _client.GetAsync<List<AllergyResponse>>($"{Me}/allergies", cancellationToken);
        var diagnoses = _client.GetAsync<List<DiagnosisResponse>>($"{Me}/diagnoses", cancellationToken);
        var prescriptions = _client.GetAsync<List<PrescriptionResponse>>($"{Me}/prescriptions", cancellationToken);
        var encounters = _client.GetAsync<List<EncounterResponse>>($"{Me}/encounters", cancellationToken);
        await Task.WhenAll(allergies, diagnoses, prescriptions, encounters);

        return new ClinicalHistoryDto(
            (await allergies).Select(ToHistoryItem).ToList(),
            (await diagnoses).Select(ToHistoryItem).ToList(),
            (await prescriptions).SelectMany(ToHistoryItems).ToList(),
            (await encounters).Select(ToEpisode).ToList());
    }

    public async Task<IReadOnlyList<ResultItemDto>> GetResultsAsync(CancellationToken cancellationToken = default) =>
        (await _client.GetAsync<List<ResultResponse>>($"{Me}/results", cancellationToken)).Select(ToResult).ToList();

    public async Task<PatientProfileDto> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        var profile = _client.GetAsync<ProfileResponse>(Me, cancellationToken);
        var summary = _client.GetAsync<SummaryResponse>($"{Me}/summary", cancellationToken);
        await Task.WhenAll(profile, summary);

        var p = await profile;
        var s = await summary;
        return new PatientProfileDto(
            $"{p.GivenNames} {p.FamilyNames}",
            p.Email,
            PatientLabels.Sex(p.Sex),
            p.DateOfBirth,
            p.Phone,
            p.Document is { } document ? $"{PatientLabels.DocumentType(document.Type)} {document.Value}" : null,
            s.EncounterCount,
            s.ResultCount,
            s.ActiveAllergies.Count);
    }

    private static EpisodeItemDto ToEpisode(EncounterResponse encounter) => new(
        string.IsNullOrWhiteSpace(encounter.Reason)
            ? PatientLabels.EncounterType(encounter.Type)
            : $"{PatientLabels.EncounterType(encounter.Type)} · {encounter.Reason}",
        encounter.ClinicName ?? PatientLabels.NoClinic(encounter.Type),
        LocalDate(encounter.StartedAtUtc),
        PatientLabels.EncounterStatus(encounter.Status));

    private static ResultItemDto ToResult(ResultResponse result) => new(
        result.Name,
        result.ResultedAtUtc is { } resultedAt ? LocalDate(resultedAt) : null,
        PatientLabels.ResultStatus(result.Status),
        Value: ResultValue(result),
        Source: PatientLabels.ResultType(result.Type),
        Note: string.IsNullOrWhiteSpace(result.ReferenceRange) ? null : $"Rango de referencia: {result.ReferenceRange}");

    private static ClinicalHistoryItemDto ToHistoryItem(AllergyResponse allergy)
    {
        var detail = $"Severidad: {PatientLabels.AllergySeverity(allergy.Severity)}";
        if (!string.IsNullOrWhiteSpace(allergy.Reaction))
        {
            detail += $" · {allergy.Reaction}";
        }

        if (allergy.Status != "Active")
        {
            detail += $" · {PatientLabels.RecordStatus(allergy.Status)}";
        }

        return new ClinicalHistoryItemDto(allergy.Substance, detail);
    }

    private static ClinicalHistoryItemDto ToHistoryItem(DiagnosisResponse diagnosis) => new(
        diagnosis.Description,
        $"{PatientLabels.DiagnosisType(diagnosis.Type)} · {PatientDisplayFormats.ShortDate(LocalDate(diagnosis.RecordedAtUtc))}");

    private static IEnumerable<ClinicalHistoryItemDto> ToHistoryItems(PrescriptionResponse prescription) =>
        prescription.Items.Select(item =>
        {
            var title = string.IsNullOrWhiteSpace(item.Concentration) ? item.MedicationName : $"{item.MedicationName} {item.Concentration}";
            var detail = $"{item.Dose.ToString("0.##", Spanish)} {item.DoseUnit} · {PatientLabels.Route(item.Route)} · {item.Frequency}";
            if (item.DurationDays is { } days)
            {
                detail += $" · {days} días";
            }

            if (prescription.Status == "Cancelled")
            {
                detail += " · Cancelada";
            }

            return new ClinicalHistoryItemDto(title, detail);
        });

    private static string? ResultValue(ResultResponse result)
    {
        if (result.NumericValue is { } value)
        {
            var number = value.ToString("0.###", Spanish);
            return string.IsNullOrWhiteSpace(result.Unit) ? number : $"{number} {result.Unit}";
        }

        return string.IsNullOrWhiteSpace(result.ResultText) ? null : result.ResultText;
    }

    private static DateOnly LocalDate(DateTimeOffset value) => DateOnly.FromDateTime(value.ToLocalTime().DateTime);

    // Respuestas de /api/mobile/patient/me (docs/api/mobile-bff-contracts.md). Los enums se leen como texto.
    private sealed record ProfileResponse(
        string GivenNames, string FamilyNames, DateOnly? DateOfBirth, string Sex, DocumentResponse? Document, string? Email, string? Phone);

    private sealed record DocumentResponse(string Type, string Value);

    private sealed record SummaryResponse(
        string DisplayName,
        int EncounterCount,
        int ResultCount,
        EncounterResponse? LatestEncounter,
        ResultResponse? LatestResult,
        List<AllergyResponse> ActiveAllergies);

    private sealed record EncounterResponse(
        Guid Id, string Type, string Status, string? Reason, DateTimeOffset StartedAtUtc, DateTimeOffset? EndedAtUtc, string? ClinicName);

    private sealed record AllergyResponse(Guid Id, string Substance, string Severity, string Status, string? Reaction, DateOnly? OnsetDate);

    private sealed record DiagnosisResponse(
        Guid Id, Guid EncounterId, string Description, string? Code, string Type, bool IsPrimary, DateTimeOffset RecordedAtUtc);

    private sealed record PrescriptionResponse(Guid Id, Guid EncounterId, string Status, List<PrescriptionItemResponse> Items);

    private sealed record PrescriptionItemResponse(
        string MedicationName, string? Concentration, decimal Dose, string DoseUnit, string Route, string Frequency, int? DurationDays);

    private sealed record ResultResponse(
        Guid Id,
        string Type,
        string Status,
        string Name,
        string? ResultText,
        decimal? NumericValue,
        string? Unit,
        string? ReferenceRange,
        DateTimeOffset? ResultedAtUtc);
}
