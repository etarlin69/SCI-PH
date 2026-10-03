namespace SCI_PH.Domain.Exceptions
{
    public sealed class InvalidClinicalTransferException : DomainException
    {
        public InvalidClinicalTransferException(string message) : base(message) { }
    }
}
