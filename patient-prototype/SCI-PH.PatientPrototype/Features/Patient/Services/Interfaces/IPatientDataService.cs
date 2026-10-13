using SCI_PH.PatientPrototype.Features.Patient.Models.Dtos;

namespace SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

/// <summary>
/// Datos del paciente autenticado. No recibe un identificador de paciente: el backend lo deducirá de la sesión, de modo
/// que la app nunca puede pedir los datos de otra persona. Hoy lo implementa el mock; después, el Mobile BFF.
/// </summary>
public interface IPatientDataService
{
    Task<PatientSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<ClinicalHistoryDto> GetHistoryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResultItemDto>> GetResultsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentItemDto>> GetDocumentsAsync(CancellationToken cancellationToken = default);

    Task<PatientProfileDto> GetProfileAsync(CancellationToken cancellationToken = default);
}
