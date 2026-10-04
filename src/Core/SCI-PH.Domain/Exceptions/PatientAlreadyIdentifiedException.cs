namespace SCI_PH.Domain.Exceptions
{
    public sealed class PatientAlreadyIdentifiedException : DomainException
    {
        public PatientAlreadyIdentifiedException(Guid encounterId)
            : base($"Encounter {encounterId} is already linked to a patient. Reassignment requires a reviewed correction workflow.")
        {
            EncounterId = encounterId;
        }

        public Guid EncounterId { get; }
    }
}
