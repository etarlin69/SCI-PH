using SCI_PH.Mobile.Features.Patient.Models.Dtos;
using SCI_PH.Mobile.Features.Patient.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Patient.Services.Mocks;

/// <summary>
/// Datos ficticios del prototipo, los mismos que antes estaban escritos en las páginas. No son datos clínicos reales.
/// Se sustituirá por un cliente del Mobile BFF que implemente <see cref="IPatientDataService"/>.
/// </summary>
public sealed class MockPatientDataService : IPatientDataService
{
    private const string PatientName = "Paciente Demo";

    private static readonly EpisodeItemDto EmergencyEpisode =
        new("Atención de emergencia", "Hospital Central", new DateOnly(2026, 9, 25), "Cerrado");

    private static readonly ResultItemDto Glucose = new(
        "Glucosa",
        new DateOnly(2026, 10, 2),
        "Publicado",
        Value: "92 mg/dL",
        Note: "Resultado registrado por el laboratorio.");

    private static readonly ResultItemDto BloodCount = new(
        "Hemograma completo",
        new DateOnly(2026, 10, 2),
        "Publicado",
        Source: "Laboratorio · Hospital Central");

    private static readonly DocumentItemDto DischargeSummary =
        new("Resumen de alta", "PDF", "Hospital Central", new DateOnly(2026, 10, 2), "📄");

    private static readonly DocumentItemDto LabReport =
        new("Resultado de laboratorio", "PDF", "Laboratorio", new DateOnly(2026, 9, 28), "📋");

    public Task<PatientSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new PatientSummaryDto(
            PatientName,
            AvailableResults: 14,
            ClinicalDocuments: 6,
            EmergencyEpisode,
            Glucose,
            DischargeSummary));

    public Task<ClinicalHistoryDto> GetHistoryAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ClinicalHistoryDto(
            Allergies: [new ClinicalHistoryItemDto("Penicilina", "Severidad moderada")],
            Conditions: [new ClinicalHistoryItemDto("Hipertensión arterial", "Registrada en 2024")],
            Medications: [new ClinicalHistoryItemDto("Losartán 50 mg", "1 tableta cada 24 horas")],
            Episodes: [EmergencyEpisode with { Title = "Emergencia prehospitalaria" }]));

    public Task<IReadOnlyList<ResultItemDto>> GetResultsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ResultItemDto>>([BloodCount, Glucose]);

    public Task<IReadOnlyList<DocumentItemDto>> GetDocumentsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DocumentItemDto>>([DischargeSummary, LabReport]);

    public Task<PatientProfileDto> GetProfileAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new PatientProfileDto(
            PatientName,
            Email: "paciente@sciph.local",
            Sex: "Masculino",
            BirthDate: new DateOnly(2000, 6, 15),
            PhoneNumber: "809-555-0101",
            Consultations: 8,
            Results: 14,
            Documents: 6));
}
