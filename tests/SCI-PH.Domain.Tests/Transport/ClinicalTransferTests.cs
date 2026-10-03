using SCI_PH.Domain.Entities.Transport;
using SCI_PH.Domain.Enums.Transport;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Tests.Transport
{
    public class ClinicalTransferTests
    {
        private static readonly DateTimeOffset At = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        private static readonly Guid Origin = Guid.NewGuid();
        private static readonly Guid Destination = Guid.NewGuid();

        private static ClinicalTransfer NewTransfer() =>
            ClinicalTransfer.Request(Guid.NewGuid(), Destination, At, Origin, reason: "Requiere UCI");

        [Fact]
        public void TransferCompletesTheFullFlow()
        {
            var transfer = NewTransfer();
            var ambulanceId = Guid.NewGuid();
            var sender = Guid.NewGuid();
            var receiver = Guid.NewGuid();

            transfer.Dispatch(sender, At.AddMinutes(10), ambulanceId);
            transfer.Receive(receiver, At.AddMinutes(45));
            transfer.Complete(At.AddMinutes(50));

            Assert.Equal(ClinicalTransferStatus.Completed, transfer.Status);
            Assert.Equal(ambulanceId, transfer.AmbulanceId);
            Assert.Equal(sender, transfer.SentByStaffId);
            Assert.Equal(receiver, transfer.ReceivedByStaffId);
            Assert.Equal(At.AddMinutes(45), transfer.ReceivedAtUtc);
        }

        [Fact]
        public void OriginAndDestinationMustDiffer()
        {
            Assert.Throws<InvalidClinicalTransferException>(() => ClinicalTransfer.Request(Guid.NewGuid(), Origin, At, Origin));
        }

        [Fact]
        public void PrehospitalTransferHasNoOrigin()
        {
            var transfer = ClinicalTransfer.Request(Guid.NewGuid(), Destination, At);

            Assert.Null(transfer.OriginClinicId);
            Assert.Equal(ClinicalTransferStatus.Requested, transfer.Status);
        }

        [Fact]
        public void StepsCannotGoBackInTime()
        {
            var transfer = NewTransfer();

            Assert.Throws<InvalidClinicalTransferException>(() => transfer.Dispatch(Guid.NewGuid(), At.AddMinutes(-1)));
            transfer.Dispatch(Guid.NewGuid(), At.AddMinutes(10));
            Assert.Throws<InvalidClinicalTransferException>(() => transfer.Receive(Guid.NewGuid(), At.AddMinutes(5)));
        }

        [Fact]
        public void StepsCannotBeSkippedAndReceivedTransfersCannotBeCancelled()
        {
            var transfer = NewTransfer();

            Assert.Throws<InvalidClinicalTransferException>(() => transfer.Receive(Guid.NewGuid(), At.AddMinutes(5)));
            Assert.Throws<InvalidClinicalTransferException>(() => transfer.Complete(At.AddMinutes(5)));

            transfer.Dispatch(Guid.NewGuid(), At.AddMinutes(1));
            transfer.Receive(Guid.NewGuid(), At.AddMinutes(30));
            Assert.Throws<InvalidClinicalTransferException>(() => transfer.Cancel(At.AddMinutes(31)));
        }

        [Fact]
        public void RequestedTransferCanBeCancelled()
        {
            var transfer = NewTransfer();

            transfer.Cancel(At.AddMinutes(2));

            Assert.Equal(ClinicalTransferStatus.Cancelled, transfer.Status);
            Assert.Equal(At.AddMinutes(2), transfer.CancelledAtUtc);
        }
    }
}
