using SCI_PH.Mobile.Common.Dialogs;
using SCI_PH.Mobile.Common.Location;
using SCI_PH.Mobile.Common.Navigation;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;
using SCI_PH.Mobile.Features.Paramedic.Services.Interfaces;

namespace SCI_PH.Mobile.Tests;

/// <summary>Registra cada navegación en lugar de usar Shell.</summary>
internal sealed class FakeNavigator : IAppNavigator
{
    public List<(string Route, IDictionary<string, object>? Parameters)> Navigations { get; } = [];

    public Task GoToAsync(string route)
    {
        Navigations.Add((route, null));
        return Task.CompletedTask;
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

internal sealed class NoLocation : IDeviceLocationService
{
    public Task<DeviceLocation?> TryGetLocationAsync() => Task.FromResult<DeviceLocation?>(null);
}

/// <summary>Cuenta las lecturas para comprobar que un flujo no exige la huella.</summary>
internal sealed class CountingFingerprintReader : IFingerprintReaderService
{
    public int Reads { get; private set; }

    public Task<FingerprintIdentifyResultDto> ReadAsync()
    {
        Reads++;
        return Task.FromResult(new FingerprintIdentifyResultDto { Found = false });
    }
}

/// <summary>Registra las peticiones de creación y puede fallar las primeras como un error transitorio de red.</summary>
internal sealed class RecordingParamedicApi : IParamedicApiService
{
    public int TransientFailuresLeft { get; set; }

    public List<CreateEmergencyDto> CreateRequests { get; } = [];

    public int IdentifyCalls { get; private set; }

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

    public Task<EmergencyDto> IdentifyAsync(Guid emergencyId, IdentifyEmergencyDto dto)
    {
        IdentifyCalls++;
        return Task.FromResult(new EmergencyDto { Id = emergencyId });
    }

    public Task RegisterVitalsAsync(Guid emergencyId, RegisterVitalsDto dto) => Task.CompletedTask;

    public Task RegisterProcedureAsync(Guid emergencyId, RegisterProcedureDto dto) => Task.CompletedTask;

    public Task RegisterMedicationAsync(Guid emergencyId, RegisterMedicationDto dto) => Task.CompletedTask;
}
