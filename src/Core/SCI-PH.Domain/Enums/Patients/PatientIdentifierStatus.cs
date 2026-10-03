namespace SCI_PH.Domain.Enums.Patients
{
    // Expiry is evaluated from ValidUntilUtc when the identifier is used; it is not stored as a status.
    public enum PatientIdentifierStatus
    {
        Active = 1,
        Revoked = 2
    }
}
