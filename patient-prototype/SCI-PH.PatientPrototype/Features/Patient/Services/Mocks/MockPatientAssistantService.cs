using SCI_PH.PatientPrototype.Features.Patient.Services.Interfaces;

namespace SCI_PH.PatientPrototype.Features.Patient.Services.Mocks;

/// <summary>
/// Respuestas educativas fijas del prototipo. Explica qué mide cada término; no interpreta valores del paciente, no
/// diagnostica ni recomienda tratamientos.
/// </summary>
public sealed class MockPatientAssistantService : IPatientAssistantService
{
    public Task<string> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        string normalized = question.ToLowerInvariant();

        if (normalized.Contains("glucosa"))
        {
            return Task.FromResult("La glucosa es el nivel de azúcar en sangre. Su interpretación depende del tipo de prueba, de si estabas en ayunas y del contexto clínico; tu profesional de salud es quien valora tu resultado.");
        }

        if (normalized.Contains("presión") || normalized.Contains("presion"))
        {
            return Task.FromResult("La presión arterial refleja la fuerza con la que la sangre circula por las arterias. Para interpretar el valor correctamente hay que revisar el resultado completo y el contexto de salud del paciente.");
        }

        if (normalized.Contains("hemoglobina"))
        {
            return Task.FromResult("La hemoglobina es una proteína de la sangre que transporta oxígeno. Su significado depende del valor reportado, la edad, el sexo y el contexto clínico.");
        }

        if (normalized.Contains("plaquetas"))
        {
            return Task.FromResult("Las plaquetas participan en la coagulación. Si tienes un resultado específico, puedo ayudarte a entender de forma general qué mide ese examen.");
        }

        return Task.FromResult("Puedo darte una explicación educativa del término o resultado que escribiste. Recuerda que esta orientación no sustituye la evaluación de un profesional de salud.");
    }
}
