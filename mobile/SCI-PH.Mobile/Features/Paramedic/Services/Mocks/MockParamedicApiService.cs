using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
using SCI_PH.Mobile.Features.Paramedic.Services;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Features.Paramedic.Services.Mocks;

public class MockParamedicApiService : IParamedicApiService
{
    private const int SimulatedDelayMs = 500;

    // "Base de datos" en memoria: solo vive mientras la app esté abierta.
    private readonly Dictionary<Guid, EmergencyDto> _emergencies = new();
    // Emula la idempotencia por clientRequestId del contrato.
    private readonly Dictionary<Guid, Guid> _createdByRequest = new();
    private readonly HashSet<Guid> _processedRequests = new();

    public async Task<EmergencyDto> CreateEmergencyAsync(CreateEmergencyDto dto)
    {
        await Task.Delay(SimulatedDelayMs);

        if (_createdByRequest.TryGetValue(dto.ClientRequestId, out var existingId))
            return _emergencies[existingId];

        var emergency = new EmergencyDto
        {
            Id = Guid.NewGuid(),
            Status = EmergencyStatus.Open,
            StartedAt = dto.StartedAt,
            Patient = null
        };

        _emergencies[emergency.Id] = emergency;
        _createdByRequest[dto.ClientRequestId] = emergency.Id;
        return emergency;
    }

    /// <summary>
    /// Emergencia creada o identificada por el servidor real (<see cref="HttpParamedicApiService"/>): los pasos que siguen
    /// en mock la usan con su id real y su estado más reciente, sin crear otra.
    /// </summary>
    public void Adopt(EmergencyDto emergency) => _emergencies[emergency.Id] = emergency;

    public async Task<EmergencyDto?> GetEmergencyAsync(Guid id)
    {
        await Task.Delay(SimulatedDelayMs);
        return _emergencies.TryGetValue(id, out var emergency) ? emergency : null;
    }

    // El modo mock no tiene asignaciones: nunca inventa emergencias activas.
    public Task<IReadOnlyList<ActiveEmergencyDto>> GetActiveEmergenciesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ActiveEmergencyDto>>([]);

    // Solo para el modo mock: cualquier documento encuentra un paciente ficticio.
    public async Task<EmergencyDto?> IdentifyByDocumentAsync(Guid emergencyId, IdentifyByDocumentDto dto)
    {
        await Task.Delay(SimulatedDelayMs);
        var emergency = GetOrThrow(emergencyId);
        emergency.Patient ??= new PatientSummaryDto { Id = Guid.NewGuid(), DisplayName = "Paciente Ficticio" };
        return emergency;
    }

    // Solo para el modo mock: cualquier documento es un candidato ficticio; la confirmación lo identifica.
    public async Task<DocumentCandidateDto> ResolveDocumentCandidateAsync(
        Guid emergencyId, PatientDocumentType documentType, string documentNumber, CancellationToken cancellationToken = default)
    {
        await Task.Delay(SimulatedDelayMs, cancellationToken);
        GetOrThrow(emergencyId);
        return new DocumentCandidateDto("Paciente Ficticio", null, documentType.ToString(), "***0000");
    }

    // Solo para el modo mock: cualquier QR con el prefijo de SCI-PH es un candidato ficticio; la identificación es la real.
    public async Task<QrCandidateDto> ResolveQrCandidateAsync(Guid emergencyId, string payload, CancellationToken cancellationToken = default)
    {
        await Task.Delay(SimulatedDelayMs, cancellationToken);
        GetOrThrow(emergencyId);
        return new QrCandidateDto("Paciente Ficticio", null, null, null);
    }

    public async Task<EmergencyDto> ConfirmQrIdentificationAsync(
        Guid emergencyId, Guid clientRequestId, string payload, CancellationToken cancellationToken = default)
    {
        await Task.Delay(SimulatedDelayMs, cancellationToken);
        var emergency = GetOrThrow(emergencyId);
        emergency.Patient ??= new PatientSummaryDto { Id = Guid.NewGuid(), DisplayName = "Paciente Ficticio" };
        return emergency;
    }

    public async Task RegisterVitalsAsync(Guid emergencyId, RegisterVitalsDto dto)
    {
        await Task.Delay(SimulatedDelayMs);

        var emergency = GetOrThrow(emergencyId);

        if (!_processedRequests.Add(dto.ClientRequestId))
            return;

        emergency.Vitals.Add(new VitalSignDto
        {
            RecordedAt = dto.RecordedAt,
            HeartRate = dto.HeartRate,
            RespiratoryRate = dto.RespiratoryRate,
            SystolicPressure = dto.SystolicPressure,
            DiastolicPressure = dto.DiastolicPressure,
            OxygenSaturation = dto.OxygenSaturation,
            Temperature = dto.Temperature,
            Glasgow = dto.Glasgow
        });
    }

    public async Task RegisterProcedureAsync(Guid emergencyId, RegisterProcedureDto dto)
    {
        await Task.Delay(SimulatedDelayMs);

        var emergency = GetOrThrow(emergencyId);

        if (!_processedRequests.Add(dto.ClientRequestId))
            return;

        emergency.Procedures.Add(new ProcedureDto
        {
            PerformedAt = dto.PerformedAt,
            ProcedureCode = dto.ProcedureCode,
            Outcome = dto.Outcome
        });
    }

    public async Task RegisterMedicationAsync(Guid emergencyId, RegisterMedicationDto dto)
    {
        await Task.Delay(SimulatedDelayMs);

        var emergency = GetOrThrow(emergencyId);

        if (!_processedRequests.Add(dto.ClientRequestId))
            return;

        emergency.Medications.Add(new MedicationDto
        {
            AdministeredAt = dto.AdministeredAt,
            MedicationId = dto.MedicationId,
            Dose = dto.Dose,
            DoseUnit = dto.DoseUnit,
            Route = dto.Route,
            Notes = dto.Notes
        });
    }

    // Solo para cumplir la interfaz: la app registra las observaciones en el servidor (HttpParamedicApiService).
    public async Task RegisterNoteAsync(Guid emergencyId, RegisterNoteDto dto)
    {
        await Task.Delay(SimulatedDelayMs);
        GetOrThrow(emergencyId);
        _processedRequests.Add(dto.ClientRequestId);
    }

    // El traslado solo existe en el servidor (HttpParamedicApiService); el servicio simulado no lo representa.
    public Task<TransferDto> RequestTransferAsync(Guid emergencyId, RequestTransferDto dto) =>
        throw new NotSupportedException("El traslado solo existe en el Mobile BFF.");

    public Task<TransferDto> StartTransferAsync(Guid emergencyId, Guid transferId) =>
        throw new NotSupportedException("El traslado solo existe en el Mobile BFF.");

    public Task<TransferDto> CancelTransferAsync(Guid emergencyId, Guid transferId) =>
        throw new NotSupportedException("El traslado solo existe en el Mobile BFF.");

    private EmergencyDto GetOrThrow(Guid id) =>
        _emergencies.TryGetValue(id, out var e)
            ? e
            : throw new InvalidOperationException("Emergencia no encontrada.");
}
