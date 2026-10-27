using SCI_PH.Mobile.Features.Paramedic.Models.Enums;

namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>Lector de huella activo (GET /api/mobile/devices/fingerprint-readers). Sin claves ni datos técnicos.</summary>
public class FingerprintReaderDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Último contacto registrado del lector; no indica si está en línea.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    public string DisplayName => $"{Name} ({Code})";
}

/// <summary>
/// Sesión de huella de la emergencia. El candidato (nombre y fecha de nacimiento) solo llega con Matched; nunca el id del
/// paciente: la confirmación la resuelve el servidor desde la sesión.
/// </summary>
public class FingerprintSessionDto
{
    public Guid Id { get; set; }
    public FingerprintSessionStatus Status { get; set; }
    public Guid ReaderId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public FingerprintFailure? Failure { get; set; }
    public string? CandidateName { get; set; }
    public DateOnly? CandidateDateOfBirth { get; set; }

    /// <summary>Hay un candidato que confirmar.</summary>
    public bool HasCandidate => Status == FingerprintSessionStatus.Matched && !string.IsNullOrWhiteSpace(CandidateName);
}
