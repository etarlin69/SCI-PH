using SCI_PH.Mobile.Features.Patient.Models.Dtos;

namespace SCI_PH.Mobile.Features.Patient.Services.Interfaces;

/// <summary>
/// Datos del paciente autenticado. No recibe un identificador de paciente: el servidor lo deduce de la sesión
/// (<c>/api/mobile/patient/me</c>, ADR-015), de modo que la app nunca puede pedir los datos de otra persona. Los fallos
/// llegan como <c>MobileApiException</c>; nunca hay datos de respaldo simulados.
/// </summary>
public interface IPatientDataService
{
    Task<PatientSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<ClinicalHistoryDto> GetHistoryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResultItemDto>> GetResultsAsync(CancellationToken cancellationToken = default);

    Task<PatientProfileDto> GetProfileAsync(CancellationToken cancellationToken = default);
}
