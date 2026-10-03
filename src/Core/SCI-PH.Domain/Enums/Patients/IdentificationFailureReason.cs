namespace SCI_PH.Domain.Enums.Patients
{
    public enum IdentificationFailureReason
    {
        NoMatch = 1,
        MultipleMatches = 2,
        LowQualityCapture = 3,
        InvalidCredential = 4,
        ProviderUnavailable = 5,
        Cancelled = 6,
        Other = 7
    }
}
