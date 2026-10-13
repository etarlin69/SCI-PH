using SCI_PH.PatientPrototype.Features.Patient.Models.Dtos;

namespace SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

/// <summary>
/// Sesión del paciente: se crea solo después del QR y la verificación facial, se lee para mostrar quién está dentro y se
/// borra al cerrar sesión. Hoy es un mock en memoria; con el Mobile BFF guardará el token en SecureStorage.
/// </summary>
public interface IPatientSessionService
{
    PatientSessionDto? CurrentSession { get; }

    bool IsAuthenticated { get; }

    /// <summary>Se dispara al crear o borrar la sesión.</summary>
    event EventHandler? SessionChanged;

    Task<PatientSessionDto> CreateSessionAsync(CancellationToken cancellationToken = default);

    Task ClearSessionAsync(CancellationToken cancellationToken = default);
}
