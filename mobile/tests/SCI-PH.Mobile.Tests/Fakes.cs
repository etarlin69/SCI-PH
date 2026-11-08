using SCI_PH.Mobile.Common.Dialogs;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Tests;

/// <summary>Catálogos vacíos, para ViewModels cuyo test no los carga.</summary>
internal sealed class EmptyParamedicCatalog : IParamedicCatalogService
{
    public Task<IReadOnlyList<CatalogItemDto>> GetProceduresAsync() => Task.FromResult<IReadOnlyList<CatalogItemDto>>([]);

    public Task<IReadOnlyList<MedicationCatalogItemDto>> GetMedicationsAsync() =>
        Task.FromResult<IReadOnlyList<MedicationCatalogItemDto>>([]);

    public Task<IReadOnlyList<TransferDestinationDto>> GetTransferDestinationsAsync() =>
        Task.FromResult<IReadOnlyList<TransferDestinationDto>>([]);
}

/// <summary>Registra cada navegación en lugar de usar Shell.</summary>
internal sealed class FakeNavigator : IAppNavigator
{
    public List<(string Route, IDictionary<string, object>? Parameters)> Navigations { get; } = [];

    /// <summary>La navegación falla (por ejemplo, la página ya no está en la pila).</summary>
    public Exception? GoToException { get; set; }

    public Task GoToAsync(string route)
    {
        Navigations.Add((route, null));
        return GoToException is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }

    public Task GoToAsync(string route, IDictionary<string, object> parameters)
    {
        Navigations.Add((route, parameters));
        return Task.CompletedTask;
    }
}

internal sealed class FakeDialogs : IAppDialogService
{
    public List<(string Title, string Message)> Shown { get; } = [];

    public Task ShowAsync(string title, string message, string accept = "OK")
    {
        Shown.Add((title, message));
        return Task.CompletedTask;
    }
}

/// <summary>Cuenta las llamadas de huella para comprobar que un flujo no la exige (y falla si se usa).</summary>
internal sealed class CountingFingerprintService : IFingerprintIdentificationService
{
    public int Calls { get; private set; }

    public Task<IReadOnlyList<FingerprintReaderDto>> GetReadersAsync(CancellationToken cancellationToken = default) =>
        Unexpected<IReadOnlyList<FingerprintReaderDto>>();

    public Task<FingerprintSessionDto> OpenSessionAsync(Guid emergencyId, Guid clientRequestId, Guid readerId, CancellationToken cancellationToken = default) =>
        Unexpected<FingerprintSessionDto>();

    public Task<FingerprintSessionDto> GetSessionAsync(Guid emergencyId, Guid sessionId, CancellationToken cancellationToken = default) =>
        Unexpected<FingerprintSessionDto>();

    public Task<EmergencyDto> ConfirmAsync(Guid emergencyId, Guid sessionId, Guid clientRequestId, CancellationToken cancellationToken = default) =>
        Unexpected<EmergencyDto>();

    public Task<FingerprintSessionDto> CancelAsync(Guid emergencyId, Guid sessionId, CancellationToken cancellationToken = default) =>
        Unexpected<FingerprintSessionDto>();

    private Task<T> Unexpected<T>()
    {
        Calls++;
        throw new InvalidOperationException("The fingerprint was not expected in this flow (test).");
    }
}

/// <summary>Registra las peticiones de creación y puede fallar las primeras como un error transitorio de red.</summary>
internal sealed class RecordingParamedicApi : IParamedicApiService
{
    public int TransientFailuresLeft { get; set; }

    public List<CreateEmergencyDto> CreateRequests { get; } = [];

    public Task<EmergencyDto> CreateEmergencyAsync(CreateEmergencyDto dto)
    {
        CreateRequests.Add(dto);
        if (TransientFailuresLeft > 0)
        {
            TransientFailuresLeft--;
            throw new HttpRequestException("Transient failure.");
        }

        return Task.FromResult(new EmergencyDto { Id = Guid.NewGuid(), StartedAt = dto.StartedAt });
    }

    public Task<EmergencyDto?> GetEmergencyAsync(Guid id) => Task.FromResult<EmergencyDto?>(null);

    public int IdentifyByDocumentCalls { get; private set; }

    public Task<EmergencyDto?> IdentifyByDocumentAsync(Guid emergencyId, IdentifyByDocumentDto dto)
    {
        IdentifyByDocumentCalls++;
        return Task.FromResult<EmergencyDto?>(new EmergencyDto { Id = emergencyId });
    }

    public Task<QrCandidateDto> ResolveQrCandidateAsync(Guid emergencyId, string payload, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The QR was not expected in this flow (test).");

    public Task<EmergencyDto> ConfirmQrIdentificationAsync(
        Guid emergencyId, Guid clientRequestId, string payload, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The QR was not expected in this flow (test).");

    public Task RegisterVitalsAsync(Guid emergencyId, RegisterVitalsDto dto) => Task.CompletedTask;

    public Task RegisterProcedureAsync(Guid emergencyId, RegisterProcedureDto dto) => Task.CompletedTask;

    public Task RegisterMedicationAsync(Guid emergencyId, RegisterMedicationDto dto) => Task.CompletedTask;

    public Task RegisterNoteAsync(Guid emergencyId, RegisterNoteDto dto) => Task.CompletedTask;

    public Task<TransferDto> RequestTransferAsync(Guid emergencyId, RequestTransferDto dto) => Task.FromResult(new TransferDto());

    public Task<TransferDto> StartTransferAsync(Guid emergencyId, Guid transferId) => Task.FromResult(new TransferDto());

    public Task<TransferDto> CancelTransferAsync(Guid emergencyId, Guid transferId) => Task.FromResult(new TransferDto());
}
