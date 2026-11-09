using System.Text.RegularExpressions;
using SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

namespace SCI_PH.Mobile.Features.Paramedic.Models;

/// <summary>
/// Cómo se nombra una emergencia en pantalla, siempre a partir de los datos del servidor. Sin identificar: el código NN es
/// lo principal. Identificada: el nombre del paciente es lo principal y el código NN se mantiene como trazabilidad (la
/// atención sigue siendo el MISMO encuentro). Un código ausente o con otra forma nunca se muestra tal cual.
/// </summary>
public sealed partial record EmergencyIdentity(string Primary, string? Secondary, bool IsIdentified)
{
    /// <summary>Texto cuando el paciente NN no tiene un código válido.</summary>
    public const string UnknownNnCode = "Paciente NN (código no disponible)";

    /// <summary>Texto cuando el paciente está identificado pero el servidor no envió su nombre.</summary>
    public const string IdentifiedWithoutName = "Paciente identificado";

    public bool HasSecondary => !string.IsNullOrWhiteSpace(Secondary);

    public static EmergencyIdentity From(EmergencyDto emergency) =>
        From(emergency.TemporaryPatientCode, emergency.Patient is null ? null : emergency.Patient.DisplayName ?? string.Empty);

    /// <param name="temporaryPatientCode">Código NN del servidor (NN- y 32 hexadecimales).</param>
    /// <param name="patientDisplayName">Null mientras no está identificado; vacío si lo está sin nombre.</param>
    public static EmergencyIdentity From(string? temporaryPatientCode, string? patientDisplayName)
    {
        var code = ValidCode(temporaryPatientCode);
        if (patientDisplayName is null)
        {
            return new EmergencyIdentity(code ?? UnknownNnCode, null, IsIdentified: false);
        }

        var name = string.IsNullOrWhiteSpace(patientDisplayName) ? IdentifiedWithoutName : patientDisplayName.Trim();
        return new EmergencyIdentity(name, code, IsIdentified: true);
    }

    /// <summary>El código normalizado si tiene la forma del servidor; si no, null.</summary>
    public static string? ValidCode(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return normalized is not null && NnCode().IsMatch(normalized) ? normalized : null;
    }

    [GeneratedRegex("^NN-[0-9A-F]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex NnCode();
}
