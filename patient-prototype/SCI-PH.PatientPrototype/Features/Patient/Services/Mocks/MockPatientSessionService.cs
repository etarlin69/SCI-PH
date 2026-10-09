using SCI_PH.PatientPrototype.Features.Patient.Models.Dtos;
using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

namespace SCI_PH.PatientPrototype.Features.Patient.Services.Mocks;

public class MockPatientSessionService : IPatientSessionService
{
    public PatientSessionDto? CurrentSession { get; private set; }

    public bool IsAuthenticated =>
        CurrentSession is not null &&
        !CurrentSession.IsExpired;

    public Task<PatientSessionDto> CreateMockSessionAsync()
    {
        CurrentSession = new PatientSessionDto
        {
            IsAuthenticated = true,
            PatientId = "PAT-0001",
            PatientName = "Paciente Demo",
            AccessToken = $"mock-token-{Guid.NewGuid():N}",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        return Task.FromResult(CurrentSession);
    }

    public Task ClearSessionAsync()
    {
        CurrentSession = null;
        return Task.CompletedTask;
    }
}