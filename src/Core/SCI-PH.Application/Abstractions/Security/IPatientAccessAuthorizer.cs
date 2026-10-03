namespace SCI_PH.Application.Abstractions.Security
{
    // Resource-based access to a patient record. Staff need the permission; a Patient account may only
    // read its own record (Patient.IdentityUserId == current user) and never gets Patients.Read globally.
    public interface IPatientAccessAuthorizer
    {
        Task<bool> CanReadAsync(Guid patientId, string permission, CancellationToken cancellationToken);

        Task<bool> CanWriteAsync(Guid patientId, string permission, CancellationToken cancellationToken);
    }
}
