namespace SCI_PH.Mobile.Features.Patient.Models.Dtos;

// Contratos de presentación de la app del paciente. No son las entidades del Domain del backend: cuando exista el
// Mobile BFF se ajustarán a sus respuestas.

/// <summary>Resumen de la pantalla de inicio.</summary>
public sealed record PatientSummaryDto(
    string DisplayName,
    int AvailableResults,
    int ClinicalDocuments,
    EpisodeItemDto? LatestEpisode,
    ResultItemDto? LatestResult,
    DocumentItemDto? LatestDocument);

/// <summary>Episodio de atención visible para el paciente.</summary>
public sealed record EpisodeItemDto(string Title, string Facility, DateOnly Date, string Status)
{
    public string DateLabel => PatientDisplayFormats.ShortDate(Date);
}

/// <summary>Historial clínico: alergias, condiciones, medicación habitual y episodios.</summary>
public sealed record ClinicalHistoryDto(
    IReadOnlyList<ClinicalHistoryItemDto> Allergies,
    IReadOnlyList<ClinicalHistoryItemDto> Conditions,
    IReadOnlyList<ClinicalHistoryItemDto> Medications,
    IReadOnlyList<EpisodeItemDto> Episodes);

/// <summary>Elemento del historial con un título y un detalle (severidad, fecha de registro, posología…).</summary>
public sealed record ClinicalHistoryItemDto(string Title, string Detail);

/// <summary>Resultado publicado. Valor, origen y nota son opcionales según el tipo de resultado.</summary>
public sealed record ResultItemDto(
    string Name,
    DateOnly PublishedOn,
    string Status,
    string? Value = null,
    string? Source = null,
    string? Note = null)
{
    public string DateLabel => PatientDisplayFormats.ShortDate(PublishedOn);
}

/// <summary>Documento clínico autorizado para el paciente. La app nunca recibe una URL permanente.</summary>
public sealed record DocumentItemDto(string Title, string Format, string Source, DateOnly IssuedOn, string Icon)
{
    public string Subtitle => $"{Format} · {Source}";

    public string DateLabel => PatientDisplayFormats.ShortDate(IssuedOn);
}

/// <summary>Datos de perfil de la cuenta del paciente.</summary>
public sealed record PatientProfileDto(
    string DisplayName,
    string? Email,
    string Sex,
    DateOnly BirthDate,
    string PhoneNumber,
    int Consultations,
    int Results,
    int Documents)
{
    public string BirthDateLabel => PatientDisplayFormats.LongDate(BirthDate);
}

/// <summary>Mensaje de la conversación con el asistente educativo.</summary>
public sealed record AssistantMessageDto(string Text, bool IsFromPatient);
