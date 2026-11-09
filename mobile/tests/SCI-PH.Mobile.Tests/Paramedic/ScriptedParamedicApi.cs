using System.Net;
using SCI_PH.Mobile.Common.Api;
using SCI_PH.Mobile.Common.Authentication;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Tests.Paramedic;

/// <summary>
/// La lista de emergencias activas y la lectura de una emergencia, controladas por el test: cada llamada puede quedar
/// pendiente (para probar respuestas tardías) o responder de inmediato con el estado actual del "servidor".
/// </summary>
internal sealed class ScriptedParamedicApi : IParamedicApiService
{
    private readonly Queue<TaskCompletionSource<IReadOnlyList<ActiveEmergencyDto>>> _pendingLists = new();
    private readonly Queue<TaskCompletionSource<EmergencyDto?>> _pendingReads = new();

    /// <summary>Lo que el servidor responde ahora a la lista (si no se dejó pendiente).</summary>
    public List<ActiveEmergencyDto> Active { get; } = [];

    /// <summary>El estado actual de cada emergencia en el servidor.</summary>
    public Dictionary<Guid, EmergencyDto> Emergencies { get; } = [];

    /// <summary>La próxima lista falla con esta excepción.</summary>
    public Exception? NextListFailure { get; set; }

    /// <summary>La próxima lectura falla con esta excepción.</summary>
    public Exception? NextReadFailure { get; set; }

    /// <summary>Las próximas listas o lecturas quedan pendientes hasta que el test las complete.</summary>
    public bool HoldLists { get; set; }

    public bool HoldReads { get; set; }

    public int ListCalls { get; private set; }

    public List<Guid> Reads { get; } = [];

    public TaskCompletionSource<IReadOnlyList<ActiveEmergencyDto>> NextPendingList() => _pendingLists.Dequeue();

    public TaskCompletionSource<EmergencyDto?> NextPendingRead() => _pendingReads.Dequeue();

    public Task<IReadOnlyList<ActiveEmergencyDto>> GetActiveEmergenciesAsync(CancellationToken cancellationToken = default)
    {
        ListCalls++;
        if (NextListFailure is { } failure)
        {
            NextListFailure = null;
            return Task.FromException<IReadOnlyList<ActiveEmergencyDto>>(failure);
        }

        if (HoldLists)
        {
            var pending = new TaskCompletionSource<IReadOnlyList<ActiveEmergencyDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingLists.Enqueue(pending);
            return pending.Task;
        }

        return Task.FromResult<IReadOnlyList<ActiveEmergencyDto>>(Active.ToList());
    }

    public Task<EmergencyDto?> GetEmergencyAsync(Guid id)
    {
        Reads.Add(id);
        if (NextReadFailure is { } failure)
        {
            NextReadFailure = null;
            return Task.FromException<EmergencyDto?>(failure);
        }

        if (HoldReads)
        {
            var pending = new TaskCompletionSource<EmergencyDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingReads.Enqueue(pending);
            return pending.Task;
        }

        return Task.FromResult(Emergencies.GetValueOrDefault(id));
    }

    public static MobileApiException Status(HttpStatusCode status) =>
        new(status switch
        {
            HttpStatusCode.Forbidden => MobileApiError.Forbidden,
            HttpStatusCode.NotFound => MobileApiError.InvalidResponse,
            HttpStatusCode.Conflict => MobileApiError.Conflict,
            HttpStatusCode.Unauthorized => MobileApiError.Unauthorized,
            _ => MobileApiError.Server
        }, status);

    public Task<EmergencyDto> CreateEmergencyAsync(CreateEmergencyDto dto) => throw Unexpected();

    /// <summary>La identificación devuelve el estado de la emergencia en el servidor (el test lo deja identificado).</summary>
    public Task<EmergencyDto?> IdentifyByDocumentAsync(Guid emergencyId, IdentifyByDocumentDto dto) =>
        Task.FromResult(Emergencies.GetValueOrDefault(emergencyId));

    public Task<QrCandidateDto> ResolveQrCandidateAsync(Guid emergencyId, string payload, CancellationToken cancellationToken = default) =>
        throw Unexpected();

    public Task<EmergencyDto> ConfirmQrIdentificationAsync(Guid emergencyId, Guid clientRequestId, string payload, CancellationToken cancellationToken = default) =>
        throw Unexpected();

    public Task RegisterVitalsAsync(Guid emergencyId, RegisterVitalsDto dto) => throw Unexpected();

    public Task RegisterProcedureAsync(Guid emergencyId, RegisterProcedureDto dto) => throw Unexpected();

    public Task RegisterMedicationAsync(Guid emergencyId, RegisterMedicationDto dto) => throw Unexpected();

    public Task RegisterNoteAsync(Guid emergencyId, RegisterNoteDto dto) => throw Unexpected();

    public Task<TransferDto> RequestTransferAsync(Guid emergencyId, RequestTransferDto dto) => throw Unexpected();

    public Task<TransferDto> StartTransferAsync(Guid emergencyId, Guid transferId) => throw Unexpected();

    public Task<TransferDto> CancelTransferAsync(Guid emergencyId, Guid transferId) => throw Unexpected();

    private static InvalidOperationException Unexpected() => new("Not expected in this flow (test).");
}

internal static class ParamedicSession
{
    public static ClinicalSessionService SignedIn(Guid? userId = null, string userName = "paramedico")
    {
        var session = new ClinicalSessionService();
        SignIn(session, userId ?? Guid.NewGuid(), userName);
        return session;
    }

    public static void SignIn(IClinicalSessionService session, Guid userId, string userName = "paramedico")
    {
        session.SetAuthentication($"access-{userId:N}", DateTimeOffset.UtcNow.AddMinutes(15));
        session.SetCurrentUser(new ClinicalUser(userId, userName, null, [], [ClinicalPermissions.EmergenciesOperate]));
    }
}
