using System.Net;
using System.Text.Json.Serialization;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;
using SCI_PH.Mobile.Features.Paramedic.Services.Mocks;

namespace SCI_PH.Mobile.Features.Paramedic.Services;

/// <summary>
/// Servicio híbrido del área paramédica. REAL (Mobile BFF, con el cliente autenticado): abrir la emergencia, leerla,
/// identificar al paciente por documento, registrar signos vitales, procedimientos, medicamentos administrados y
/// observaciones, y el traslado (solicitar, iniciar, cancelar). La identificación por huella es real y tiene su propio
/// servicio (<see cref="HttpFingerprintIdentificationService"/>). Los fallos de las llamadas reales llegan como
/// <see cref="MobileApiException"/>.
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

    // Una toma: 1 a 7 valores en una sola petición, todo o nada. 201 (nueva) o 200 (reintento con la misma clave y los
    // mismos datos): en ambos casos quedó registrada una sola vez. Solo viajan los valores capturados; la unidad la pone
    // el servidor. 400/403/409 y el resto llegan como MobileApiException.
    public async Task RegisterVitalsAsync(Guid emergencyId, RegisterVitalsDto dto)
    {
        var request = new VitalSignsRequest(
            dto.ClientRequestId, ToUtc(dto.RecordedAt), dto.HeartRate, dto.RespiratoryRate, dto.SystolicPressure,
            dto.DiastolicPressure, dto.OxygenSaturation, dto.Temperature, dto.Glasgow);
        await _client.PostAsync<VitalSignsRequest, VitalSignsResponse>($"{Emergencies}/{emergencyId}/vital-signs", request);
    }

    // Un procedimiento ya realizado: un código del catálogo del servidor, su hora y el resultado si lo hay. 201 (nuevo) o 200
    // (reintento con la misma clave y los mismos datos): en ambos casos quedó registrado una sola vez. El nombre, la
    // descripción y el sistema de códigos los pone el servidor. 400/403/404/409 y el resto llegan como MobileApiException.
    public async Task RegisterProcedureAsync(Guid emergencyId, RegisterProcedureDto dto)
    {
        var request = new ProcedureRequest(dto.ClientRequestId, dto.ProcedureCode, ToUtc(dto.PerformedAt), dto.Outcome);
        await _client.PostAsync<ProcedureRequest, ProcedureResponse>($"{Emergencies}/{emergencyId}/procedures", request);
    }

    // Una dosis ya administrada, sin prescripción: el medicamento (id del catálogo), la dosis, la unidad, la vía (nombre del
    // enum), su hora y las notas si las hay. 201 (nueva) o 200 (reintento con la misma clave y los mismos datos): en ambos
    // casos quedó registrada una sola vez. 400/403/404/409 y el resto llegan como MobileApiException.
    public async Task RegisterMedicationAsync(Guid emergencyId, RegisterMedicationDto dto)
    {
        var request = new MedicationAdministrationRequest(
            dto.ClientRequestId, dto.MedicationId, dto.Dose, dto.DoseUnit, dto.Route, ToUtc(dto.AdministeredAt), dto.Notes);
        await _client.PostAsync<MedicationAdministrationRequest, MedicationAdministrationResponse>(
            $"{Emergencies}/{emergencyId}/medication-administrations", request);
    }

    // Una observación: su hora y su texto. El tipo (Prehospital) y el autor los pone el servidor. 201 (nueva) o 200
    // (reintento con la misma clave y los mismos datos): en ambos casos quedó registrada una sola vez. 400/403/404/409 y el
    // resto llegan como MobileApiException.
    public async Task RegisterNoteAsync(Guid emergencyId, RegisterNoteDto dto)
    {
        var request = new NoteRequest(dto.ClientRequestId, ToUtc(dto.OccurredAt), dto.Content);
        await _client.PostAsync<NoteRequest, NoteResponse>($"{Emergencies}/{emergencyId}/notes", request);
    }

    // Traslado a una clínica del catálogo: sin origen, horas ni personal (los pone el servidor). 201 (nuevo) o 200
    // (reintento con la misma clave y los mismos datos): el mismo traslado. 400/403/404/409 llegan como MobileApiException.
    public async Task<TransferDto> RequestTransferAsync(Guid emergencyId, RequestTransferDto dto)
    {
        var request = new TransferRequest(dto.ClientRequestId, dto.DestinationClinicId, dto.Reason, dto.Notes);
        return ToDto(await _client.PostAsync<TransferRequest, TransferResponse>($"{Emergencies}/{emergencyId}/transfers", request));
    }

    // Siempre sobre ESE traslado: un reintento tardío nunca toca otro traslado de la emergencia.
    public async Task<TransferDto> StartTransferAsync(Guid emergencyId, Guid transferId) =>
        ToDto(await _client.PostAsync<object, TransferResponse>($"{Emergencies}/{emergencyId}/transfers/{transferId}/start", new { }));

    public async Task<TransferDto> CancelTransferAsync(Guid emergencyId, Guid transferId) =>
        ToDto(await _client.PostAsync<object, TransferResponse>($"{Emergencies}/{emergencyId}/transfers/{transferId}/cancel", new { }));

    private static DateTimeOffset ToUtc(DateTime value) =>
        new(value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime());

    private static EmergencyDto ToDto(EmergencyResponse response) => new()
    {
        Id = response.Id,
        TemporaryPatientCode = response.TemporaryPatientCode,
        Status = ToStatus(response.Status),
        StartedAt = response.StartedAtUtc.UtcDateTime,
        Patient = response.Patient is null ? null : new PatientSummaryDto { Id = response.Patient.Id, DisplayName = response.Patient.DisplayName },
        ActiveTransfer = response.ActiveTransfer is null ? null : ToDto(response.ActiveTransfer)
    };

    private static TransferDto ToDto(TransferResponse response) => new()
    {
        Id = response.Id,
        Status = response.Status,
        Destination = response.Destination,
        RequestedAt = response.RequestedAtUtc.UtcDateTime,
        SentAt = response.SentAtUtc?.UtcDateTime,
        CancelledAt = response.CancelledAtUtc?.UtcDateTime
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

    // Contrato exacto de la toma: los valores ausentes no se envían.
    private sealed record VitalSignsRequest(
        Guid ClientRequestId,
        DateTimeOffset RecordedAt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? HeartRate,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? RespiratoryRate,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SystolicPressure,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? DiastolicPressure,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? OxygenSaturation,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Temperature,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Glasgow);

    private sealed record VitalSignsResponse(Guid EncounterId, DateTimeOffset RecordedAtUtc);

    // Contrato exacto del procedimiento: sin resultado, el campo no se envía.
    private sealed record ProcedureRequest(
        Guid ClientRequestId,
        string ProcedureCode,
        DateTimeOffset PerformedAt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Outcome);

    private sealed record ProcedureResponse(Guid Id, Guid EncounterId, string Code);

    // Contrato exacto de la dosis: sin prescripción ni datos del medicamento; sin notas, el campo no se envía.
    private sealed record MedicationAdministrationRequest(
        Guid ClientRequestId,
        Guid MedicationId,
        decimal Dose,
        string DoseUnit,
        MedicationRoute Route,
        DateTimeOffset AdministeredAt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Notes);

    private sealed record MedicationAdministrationResponse(Guid Id, Guid EncounterId, Guid MedicationId);

    // Contrato exacto de la nota: sin tipo, autor ni paciente.
    private sealed record NoteRequest(Guid ClientRequestId, DateTimeOffset OccurredAt, string Content);

    private sealed record NoteResponse(Guid Id, Guid EncounterId, string Type);

    private sealed record EmergencyResponse(
        Guid Id,
        string? TemporaryPatientCode,
        string? Status,
        DateTimeOffset StartedAtUtc,
        string? ChiefComplaint,
        EmergencyPatient? Patient,
        TransferResponse? ActiveTransfer = null);

    // Contrato exacto de la solicitud: sin origen, horas, estado ni personal; sin motivo o notas, el campo no se envía.
    private sealed record TransferRequest(
        Guid ClientRequestId,
        Guid DestinationClinicId,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Notes);

    private sealed record TransferResponse(
        Guid Id,
        TransferStatus Status,
        TransferDestinationDto Destination,
        DateTimeOffset RequestedAtUtc,
        DateTimeOffset? SentAtUtc,
        DateTimeOffset? CancelledAtUtc);

    private sealed record EmergencyPatient(Guid Id, string? DisplayName);
}
