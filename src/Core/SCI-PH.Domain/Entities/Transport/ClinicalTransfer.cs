using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Enums.Transport;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Entities.Transport
{
    // Movement of an encounter to another clinic: Requested -> InTransit -> Received -> Completed,
    // or Cancelled before reception. Application keeps the encounter status in step in the same transaction.
    public sealed class ClinicalTransfer
    {
        // An encounter can have only one transfer in these statuses at a time (enforced by a filtered unique index).
        // Received, Completed and Cancelled transfers do not block a later transfer.
        public static IReadOnlyCollection<ClinicalTransferStatus> ActiveStatuses { get; } =
            [ClinicalTransferStatus.Requested, ClinicalTransferStatus.InTransit];

        private ClinicalTransfer() { }

        public Guid Id { get; private set; }
        public Guid ClinicalEncounterId { get; private set; }
        public ClinicalEncounter Encounter { get; private set; } = null!;
        // Null when the patient is picked up outside a clinic (prehospital care).
        public Guid? OriginClinicId { get; private set; }
        public Clinic? OriginClinic { get; private set; }
        public Guid DestinationClinicId { get; private set; }
        public Clinic DestinationClinic { get; private set; } = null!;
        public Guid? AmbulanceId { get; private set; }
        public Ambulance? Ambulance { get; private set; }
        public ClinicalTransferStatus Status { get; private set; }
        public string? Reason { get; private set; }
        public string? Notes { get; private set; }
        public DateTimeOffset RequestedAtUtc { get; private set; }
        public DateTimeOffset? SentAtUtc { get; private set; }
        public Guid? SentByStaffId { get; private set; }
        public DateTimeOffset? ReceivedAtUtc { get; private set; }
        public Guid? ReceivedByStaffId { get; private set; }
        public DateTimeOffset? CompletedAtUtc { get; private set; }
        public DateTimeOffset? CancelledAtUtc { get; private set; }

        public static ClinicalTransfer Request(
            Guid clinicalEncounterId,
            Guid destinationClinicId,
            DateTimeOffset requestedAtUtc,
            Guid? originClinicId = null,
            Guid? ambulanceId = null,
            string? reason = null,
            string? notes = null)
        {
            Guard.Id(destinationClinicId, nameof(destinationClinicId));
            if (originClinicId == destinationClinicId)
            {
                throw new InvalidClinicalTransferException("The destination clinic must be different from the origin clinic.");
            }

            return new ClinicalTransfer
            {
                Id = Guid.NewGuid(),
                ClinicalEncounterId = Guard.Id(clinicalEncounterId, nameof(clinicalEncounterId)),
                DestinationClinicId = destinationClinicId,
                OriginClinicId = Guard.OptionalId(originClinicId, nameof(originClinicId)),
                AmbulanceId = Guard.OptionalId(ambulanceId, nameof(ambulanceId)),
                Reason = Guard.OptionalText(reason, FieldLengths.ShortText, nameof(reason)),
                Notes = Guard.OptionalText(notes, FieldLengths.LongText, nameof(notes)),
                RequestedAtUtc = Guard.Utc(requestedAtUtc),
                Status = ClinicalTransferStatus.Requested
            };
        }

        public void Dispatch(Guid sentByStaffId, DateTimeOffset sentAtUtc, Guid? ambulanceId = null)
        {
            EnsureStatus(ClinicalTransferStatus.Requested, nameof(Dispatch));
            SentByStaffId = Guard.Id(sentByStaffId, nameof(sentByStaffId));
            SentAtUtc = NotBefore(sentAtUtc, RequestedAtUtc, nameof(sentAtUtc));
            AmbulanceId = Guard.OptionalId(ambulanceId, nameof(ambulanceId)) ?? AmbulanceId;
            Status = ClinicalTransferStatus.InTransit;
        }

        public void Receive(Guid receivedByStaffId, DateTimeOffset receivedAtUtc)
        {
            EnsureStatus(ClinicalTransferStatus.InTransit, nameof(Receive));
            ReceivedByStaffId = Guard.Id(receivedByStaffId, nameof(receivedByStaffId));
            ReceivedAtUtc = NotBefore(receivedAtUtc, SentAtUtc!.Value, nameof(receivedAtUtc));
            Status = ClinicalTransferStatus.Received;
        }

        public void Complete(DateTimeOffset completedAtUtc)
        {
            EnsureStatus(ClinicalTransferStatus.Received, nameof(Complete));
            CompletedAtUtc = NotBefore(completedAtUtc, ReceivedAtUtc!.Value, nameof(completedAtUtc));
            Status = ClinicalTransferStatus.Completed;
        }

        public void Cancel(DateTimeOffset cancelledAtUtc)
        {
            if (Status is not (ClinicalTransferStatus.Requested or ClinicalTransferStatus.InTransit))
            {
                throw new InvalidClinicalTransferException($"A {Status} transfer cannot be cancelled.");
            }

            CancelledAtUtc = NotBefore(cancelledAtUtc, RequestedAtUtc, nameof(cancelledAtUtc));
            Status = ClinicalTransferStatus.Cancelled;
        }

        private void EnsureStatus(ClinicalTransferStatus expected, string operation)
        {
            if (Status != expected)
            {
                throw new InvalidClinicalTransferException($"{operation} requires a {expected} transfer; current status is {Status}.");
            }
        }

        private static DateTimeOffset NotBefore(DateTimeOffset value, DateTimeOffset previous, string paramName) =>
            value < previous
                ? throw new InvalidClinicalTransferException($"{paramName} cannot be earlier than the previous transfer step.")
                : value.ToUniversalTime();
    }
}
