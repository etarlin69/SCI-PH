namespace SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

/// <summary>Asistente educativo SCI-PH AI: explica términos en lenguaje sencillo; no diagnostica ni prescribe.</summary>
public interface IPatientAssistantService
{
    Task<string> AskAsync(string question, CancellationToken cancellationToken = default);
}
