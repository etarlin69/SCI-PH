using System.Net;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;

namespace SCI_PH.Mobile.Features.Paramedic.Services;

/// <summary>
/// Servicio híbrido del área paramédica. REAL (Mobile BFF, con el cliente autenticado): abrir la emergencia, leerla e
/// identificar al paciente por documento. Sigue en MOCK: identificación por huella, signos vitales, procedimientos y
/// medicamentos; trabajan sobre el id REAL que devuelve el servidor, sin crear otra emergencia. Los fallos de las
/// llamadas reales llegan como <see cref="MobileApiException"/>.
/// </summary>
public sealed class HttpParamedicApiService : IParamedicApiService
{
    private const string Emergencies = "api/mobile/emergencies";

    private readonly IMobileApiClient _client;
    private readonly MockParamedicApiService _mock;

    public HttpParamedicApiService(IMobileApiClient client, MockParamedicApiService mock)
    {
        _client = client;
        _mock = mock;
    }

    // 201 (nueva) y 200 (reintento con la misma clave y los mismos datos) devuelven la misma forma.
    public async Task<EmergencyDto> CreateEmergencyAsync(CreateEmergencyDto dto)
    {
        var request = new OpenEmergencyRequest(dto.ClientRequestId, ToUtc(dto.StartedAt), dto.ChiefComplaint);
        var emergency = ToDto(await _client.PostAsync<OpenEmergencyRequest, EmergencyResponse>(Emergencies, request));
        _mock.Adopt(emergency);
        return emergency;
    }

    public async Task<EmergencyDto?> GetEmergencyAsync(Guid id)
    {
        try
        {
            return ToDto(await _client.GetAsync<EmergencyResponse>($"{Emergencies}/{id}"));
        }
        catch (MobileApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // Huella (y cualquier otro método): sigue en mock.
    public Task<EmergencyDto> IdentifyAsync(Guid emergencyId, IdentifyEmergencyDto dto) => _mock.IdentifyAsync(emergencyId, dto);

    // 200: la MISMA emergencia, ya identificada (también al reintentar con la misma clave). 404: ningún paciente tiene ese
    // documento; la emergencia sigue NN (null). 409 y el resto llegan como MobileApiException.
    public async Task<EmergencyDto?> IdentifyByDocumentAsync(Guid emergencyId, IdentifyByDocumentDto dto)
    {
        var request = new IdentifyByDocumentRequest(dto.ClientRequestId, dto.DocumentType, dto.DocumentNumber);
        try
        {
            var emergency = ToDto(await _client.PostAsync<IdentifyByDocumentRequest, EmergencyResponse>(
                $"{Emergencies}/{emergencyId}/identify/document", request));
            _mock.Adopt(emergency);
            return emergency;
        }
        catch (MobileApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task RegisterVitalsAsync(Guid emergencyId, RegisterVitalsDto dto) => _mock.RegisterVitalsAsync(emergencyId, dto);

    public Task RegisterProcedureAsync(Guid emergencyId, RegisterProcedureDto dto) => _mock.RegisterProcedureAsync(emergencyId, dto);

    public Task RegisterMedicationAsync(Guid emergencyId, RegisterMedicationDto dto) => _mock.RegisterMedicationAsync(emergencyId, dto);

    private static DateTimeOffset ToUtc(DateTime value) =>
        new(value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime());

    private static EmergencyDto ToDto(EmergencyResponse response) => new()
    {
        Id = response.Id,
        TemporaryPatientCode = response.TemporaryPatientCode,
        Status = ToStatus(response.Status),
        StartedAt = response.StartedAtUtc.UtcDateTime,
        Patient = response.Patient is null ? null : new PatientSummaryDto { Id = response.Patient.Id, DisplayName = response.Patient.DisplayName }
    };

    // Estados de la atención en el servidor (texto) → estados que muestra la app.
    private static EmergencyStatus ToStatus(string? status) => status switch
    {
        "Transferred" => EmergencyStatus.Transferred,
        "Closed" or "Cancelled" => EmergencyStatus.Closed,
        _ => EmergencyStatus.Open
    };

    // Contrato V1 exacto: sin ubicación, clínica, personal ni paciente (el servidor rechaza cualquier otro campo).
    private sealed record OpenEmergencyRequest(Guid ClientRequestId, DateTimeOffset StartedAt, string ChiefComplaint);

    private sealed record IdentifyByDocumentRequest(Guid ClientRequestId, PatientDocumentType DocumentType, string DocumentNumber);

    private sealed record EmergencyResponse(
        Guid Id,
        string? TemporaryPatientCode,
        string? Status,
        DateTimeOffset StartedAtUtc,
        string? ChiefComplaint,
        EmergencyPatient? Patient);

    private sealed record EmergencyPatient(Guid Id, string? DisplayName);
}
