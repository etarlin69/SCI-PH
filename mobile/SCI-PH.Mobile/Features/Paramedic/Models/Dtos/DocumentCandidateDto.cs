namespace SCI_PH.Mobile.Features.Paramedic.Models.Dtos;

/// <summary>
/// Candidato de <c>POST api/mobile/emergencies/{id}/identify/document/resolve</c>: solo lo necesario para que el paramédico lo
/// confirme (nombre, fecha de nacimiento y el documento buscado, enmascarado por el servidor). Sin id de paciente ni datos
/// clínicos. Mostrarlo no identifica la emergencia: solo la confirmación lo hace.
/// </summary>
public sealed record DocumentCandidateDto(string DisplayName, DateOnly? DateOfBirth, string? DocumentType, string? DocumentValue)
{
    public override string ToString() => nameof(DocumentCandidateDto);
}
