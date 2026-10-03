namespace SCI_PH.Domain.Exceptions
{
    public sealed class InvalidPatientIdentifierException : DomainException
    {
        public InvalidPatientIdentifierException(string message) : base(message) { }
    }
}
