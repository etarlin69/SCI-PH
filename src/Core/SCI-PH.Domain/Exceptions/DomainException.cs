namespace SCI_PH.Domain.Exceptions
{
    // Raised when an operation would break a clinical business rule.
    public class DomainException : Exception
    {
        public DomainException(string message) : base(message) { }
    }
}
