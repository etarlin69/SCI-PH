using SCI_PH.PatientPrototype.Features.Patient.Models.Dtos;
using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

namespace SCI_PH.PatientPrototype.Features.Patient.Services.Mocks;

/// <summary>Sesión simulada en memoria. El token es un valor de prueba, no un JWT, y nunca se persiste.</summary>
public sealed class MockPatientSessionService : IPatientSessionService
{
    private readonly TimeProvider _timeProvider;

    public MockPatientSessionService(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public PatientSessionDto? CurrentSession { get; private set; }

    public bool IsAuthenticated =>
        CurrentSession is not null &&
        !CurrentSession.IsExpiredAt(_timeProvider.GetUtcNow().UtcDateTime);

    public event EventHandler? SessionChanged;

    public Task<PatientSessionDto> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        CurrentSession = new PatientSessionDto
        {
            IsAuthenticated = true,
            PatientId = "PAT-0001",
            PatientName = "Paciente Demo",
            AccessToken = $"mock-token-{Guid.NewGuid():N}",
            ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(1)
        };

        SessionChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(CurrentSession);
    }

    public Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        CurrentSession = null;
        SessionChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}
