using SCI_PH.PatientPrototype.Features.Patient.Models.Dtos;

namespace SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

public interface IPatientSessionService
{
    PatientSessionDto? CurrentSession { get; }

    bool IsAuthenticated { get; }

    Task<PatientSessionDto> CreateMockSessionAsync();

    Task ClearSessionAsync();
}