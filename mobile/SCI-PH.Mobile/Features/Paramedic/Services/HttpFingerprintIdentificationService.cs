using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.Services;

/// <summary>
/// Identificación por huella REAL (Mobile BFF, con el cliente autenticado). La confirmación no envía paciente: el servidor
/// lo toma de la sesión. Los fallos llegan como <see cref="MobileApiException"/>.
/// </summary>
public sealed class HttpFingerprintIdentificationService : IFingerprintIdentificationService
{
    private const string Readers = "api/mobile/devices/fingerprint-readers";

    private readonly IMobileApiClient _client;

    public HttpFingerprintIdentificationService(IMobileApiClient client) => _client = client;

    private static string Sessions(Guid emergencyId) => $"api/mobile/emergencies/{emergencyId}/identify/fingerprint/sessions";

    public async Task<IReadOnlyList<FingerprintReaderDto>> GetReadersAsync(CancellationToken cancellationToken = default) =>
        (await _client.GetAsync<List<ReaderResponse>>(Readers, cancellationToken))
            .Select(r => new FingerprintReaderDto { Id = r.Id, Code = r.Code, Name = r.Name, LastSeenAt = r.LastSeenAtUtc })
            .ToList();

    public async Task<FingerprintSessionDto> OpenSessionAsync(
        Guid emergencyId, Guid clientRequestId, Guid readerId, CancellationToken cancellationToken = default) =>
        ToDto(await _client.PostAsync<OpenRequest, SessionResponse>(
            Sessions(emergencyId), new OpenRequest(clientRequestId, readerId), cancellationToken));

    public async Task<FingerprintSessionDto> GetSessionAsync(Guid emergencyId, Guid sessionId, CancellationToken cancellationToken = default) =>
        ToDto(await _client.GetAsync<SessionResponse>($"{Sessions(emergencyId)}/{sessionId}", cancellationToken));

    public async Task<EmergencyDto> ConfirmAsync(
        Guid emergencyId, Guid sessionId, Guid clientRequestId, CancellationToken cancellationToken = default)
    {
        var emergency = await _client.PostAsync<ConfirmRequest, ConfirmedEmergencyResponse>(
            $"{Sessions(emergencyId)}/{sessionId}/confirm", new ConfirmRequest(clientRequestId), cancellationToken);
        return new EmergencyDto
        {
            Id = emergency.Id,
            TemporaryPatientCode = emergency.TemporaryPatientCode,
            Patient = emergency.Patient is null ? null : new PatientSummaryDto { Id = emergency.Patient.Id, DisplayName = emergency.Patient.DisplayName }
        };
    }

    public async Task<FingerprintSessionDto> CancelAsync(Guid emergencyId, Guid sessionId, CancellationToken cancellationToken = default) =>
        ToDto(await _client.PostAsync<object, SessionResponse>($"{Sessions(emergencyId)}/{sessionId}/cancel", new { }, cancellationToken));

    private static FingerprintSessionDto ToDto(SessionResponse response) => new()
    {
        Id = response.Id,
        Status = response.Status,
        ReaderId = response.ReaderId,
        ExpiresAt = response.ExpiresAtUtc,
        Failure = response.Failure,
        CandidateName = response.Candidate?.DisplayName,
        CandidateDateOfBirth = response.Candidate?.DateOfBirth
    };

    private sealed record ReaderResponse(Guid Id, string Code, string Name, DateTimeOffset? LastSeenAtUtc);

    private sealed record OpenRequest(Guid ClientRequestId, Guid DeviceId);

    private sealed record ConfirmRequest(Guid ClientRequestId);

    private sealed record CandidateResponse(string DisplayName, DateOnly? DateOfBirth);

    private sealed record SessionResponse(
        Guid Id, FingerprintSessionStatus Status, Guid ReaderId, DateTimeOffset ExpiresAtUtc, FingerprintFailure? Failure, CandidateResponse? Candidate);

    private sealed record PatientResponse(Guid Id, string DisplayName);

    private sealed record ConfirmedEmergencyResponse(Guid Id, string? TemporaryPatientCode, PatientResponse? Patient);
}
