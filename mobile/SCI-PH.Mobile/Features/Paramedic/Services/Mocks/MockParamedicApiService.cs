using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Models.Enums;
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

    public async Task<EmergencyDto?> GetEmergencyAsync(Guid id)
    {
        await Task.Delay(SimulatedDelayMs);
        return _emergencies.TryGetValue(id, out var emergency) ? emergency : null;
    }

    public async Task<EmergencyDto> IdentifyAsync(Guid emergencyId, IdentifyEmergencyDto dto)
    {
        await Task.Delay(SimulatedDelayMs);

        var emergency = GetOrThrow(emergencyId);

        if (!_processedRequests.Add(dto.ClientRequestId))
            return emergency;

        // 409 del contrato: ya identificada con otro paciente.
        if (emergency.Patient is not null && dto.PatientId is not null && emergency.Patient.Id != dto.PatientId)
            throw new InvalidOperationException("La emergencia ya está identificada con otro paciente.");

        emergency.Patient = new PatientSummaryDto
        {
            Id = dto.PatientId ?? Guid.NewGuid(),
            DisplayName = dto.Method == IdentificationMethod.Manual
                ? "Paciente (registro manual)"
                : "Paciente Ficticio"
        };

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
            RecordedAt = dto.RecordedAt,
            ProcedureCode = dto.ProcedureCode,
            Notes = dto.Notes
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
            RecordedAt = dto.RecordedAt,
            MedicationCode = dto.MedicationCode,
            Dose = dto.Dose,
            Route = dto.Route,
            Notes = dto.Notes
        });
    }

    private EmergencyDto GetOrThrow(Guid id) =>
        _emergencies.TryGetValue(id, out var e)
            ? e
            : throw new InvalidOperationException("Emergencia no encontrada.");
}
