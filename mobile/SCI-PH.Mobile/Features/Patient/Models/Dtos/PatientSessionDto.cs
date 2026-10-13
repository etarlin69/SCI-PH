namespace SCI_PH.Mobile.Features.Patient.Models.Dtos;

public class PatientSessionDto
{
    public bool IsAuthenticated { get; set; }

    public string PatientId { get; set; } = string.Empty;

    public string PatientName { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public bool IsExpiredAt(DateTime utcNow) =>
        !IsAuthenticated || utcNow >= ExpiresAt;
}
