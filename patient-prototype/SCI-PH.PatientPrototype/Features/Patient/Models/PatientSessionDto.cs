namespace SCI_PH.PatientPrototype.Features.Patient.Models.Dtos;

public class PatientSessionDto
{
    public bool IsAuthenticated { get; set; }

    public string PatientId { get; set; } = string.Empty;

    public string PatientName { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public bool IsExpired =>
        !IsAuthenticated || DateTime.UtcNow >= ExpiresAt;
}