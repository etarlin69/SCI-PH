namespace SCI_PH.Domain.Exceptions
{
    public sealed class InvalidClinicalEncounterStateException : DomainException
    {
        public InvalidClinicalEncounterStateException(string message) : base(message) { }
    }
}
