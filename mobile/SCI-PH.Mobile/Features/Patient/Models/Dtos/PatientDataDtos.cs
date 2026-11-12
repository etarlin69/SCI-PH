namespace SCI_PH.Mobile.Features.Patient.Models.Dtos;

// Contratos de presentación de la app del paciente, construidos desde las respuestas de /api/mobile/patient/me
// (HttpPatientDataService). No son las entidades del Domain ni los contratos del Mobile BFF.

/// <summary>Resumen de la pantalla de inicio.</summary>
public sealed record PatientSummaryDto(
    string DisplayName,
    int AvailableResults,
    int Encounters,
    EpisodeItemDto? LatestEpisode,
    ResultItemDto? LatestResult);

/// <summary>Atención (episodio) del paciente. El id solo sirve para abrir su detalle (una atención propia).</summary>
public sealed record EpisodeItemDto(Guid Id, string Title, string Facility, DateOnly Date, string Status)
{
    public string DateLabel => PatientDisplayFormats.ShortDate(Date);
}

/// <summary>
/// Detalle de una atención propia: sus datos y lo publicado en ella (diagnósticos, resultados y recetas), con las mismas
/// reglas de visibilidad que el historial. Las notas del personal, los procedimientos y los datos internos no se publican.
/// </summary>
public sealed record EncounterDetailDto(
    EpisodeItemDto Episode,
    DateOnly? EndedOn,
    IReadOnlyList<ClinicalHistoryItemDto> Diagnoses,
    IReadOnlyList<ResultItemDto> Results,
    IReadOnlyList<ClinicalHistoryItemDto> Prescriptions)
{
    public string PeriodLabel => EndedOn is { } ended && ended != Episode.Date
        ? $"{Episode.DateLabel} – {PatientDisplayFormats.ShortDate(ended)}"
        : Episode.DateLabel;
}

/// <summary>Historial clínico: alergias, diagnósticos, recetas y atenciones.</summary>
public sealed record ClinicalHistoryDto(
    IReadOnlyList<ClinicalHistoryItemDto> Allergies,
    IReadOnlyList<ClinicalHistoryItemDto> Diagnoses,
    IReadOnlyList<ClinicalHistoryItemDto> Prescriptions,
    IReadOnlyList<EpisodeItemDto> Episodes);

/// <summary>Elemento del historial con un título y un detalle (severidad, fecha de registro, posología…).</summary>
public sealed record ClinicalHistoryItemDto(string Title, string Detail);

/// <summary>Resultado publicado. Valor, origen y nota son opcionales según el tipo de resultado.</summary>
public sealed record ResultItemDto(
    string Name,
    DateOnly? PublishedOn,
    string Status,
    string? Value = null,
    string? Source = null,
    string? Note = null)
{
    public string DateLabel => PublishedOn is { } date ? PatientDisplayFormats.ShortDate(date) : "Sin fecha";
}

/// <summary>Datos de perfil del paciente. El documento llega enmascarado desde el servidor.</summary>
public sealed record PatientProfileDto(
    string DisplayName,
    string? Email,
    string Sex,
    DateOnly? BirthDate,
    string? PhoneNumber,
    string? Document,
    int Encounters,
    int Results,
    int ActiveAllergies)
{
    public string BirthDateLabel => BirthDate is { } date ? PatientDisplayFormats.LongDate(date) : "—";
}
