using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Entities.Medications
{
    // Clinical medication order written during an encounter. The patient is derived from the encounter,
    // so there is no second PatientId that could contradict it. It does not imply dispensing or intake.
    public sealed class Prescription
    {
        private readonly List<PrescriptionDetail> _details = [];

        private Prescription() { }

        public Guid Id { get; private set; }
        public Guid ClinicalEncounterId { get; private set; }
        public ClinicalEncounter Encounter { get; private set; } = null!;
        public Guid PrescribedByStaffId { get; private set; }
        public PrescriptionStatus Status { get; private set; }
        public string? Notes { get; private set; }
        public DateTimeOffset CreatedAtUtc { get; private set; }
        public DateTimeOffset? IssuedAtUtc { get; private set; }
        public DateTimeOffset? ValidUntilUtc { get; private set; }
        public DateTimeOffset? CancelledAtUtc { get; private set; }

        public IReadOnlyCollection<PrescriptionDetail> Details => _details.AsReadOnly();

        public static Prescription Draft(Guid clinicalEncounterId, Guid prescribedByStaffId, string? notes = null) =>
            new()
            {
                Id = Guid.NewGuid(),
                ClinicalEncounterId = Guard.Id(clinicalEncounterId, nameof(clinicalEncounterId)),
                PrescribedByStaffId = Guard.Id(prescribedByStaffId, nameof(prescribedByStaffId)),
                Notes = Guard.OptionalText(notes, FieldLengths.LongText, nameof(notes)),
                Status = PrescriptionStatus.Draft,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

        public PrescriptionDetail AddDetail(
            Guid medicationId,
            decimal dose,
            string doseUnit,
            MedicationRoute route,
            string frequency,
            int? durationDays = null,
            string? instructions = null)
        {
            EnsureDraft();
            var detail = new PrescriptionDetail(Id, medicationId, dose, doseUnit, route, frequency, durationDays, instructions);
            _details.Add(detail);
            return detail;
        }

        public void Issue(DateTimeOffset issuedAtUtc, DateTimeOffset? validUntilUtc = null)
        {
            EnsureDraft();
            if (_details.Count == 0)
            {
                throw new DomainException("A prescription needs at least one medication before it is issued.");
            }

            var issuedAt = Guard.Utc(issuedAtUtc);
            if (validUntilUtc <= issuedAt)
            {
                throw new ArgumentOutOfRangeException(nameof(validUntilUtc), validUntilUtc, "Validity must end after issue.");
            }

            IssuedAtUtc = issuedAt;
            ValidUntilUtc = validUntilUtc?.ToUniversalTime();
            Status = PrescriptionStatus.Issued;
        }

        public void Cancel(DateTimeOffset cancelledAtUtc)
        {
            if (Status == PrescriptionStatus.Cancelled)
            {
                throw new DomainException("The prescription is already cancelled.");
            }

            CancelledAtUtc = Guard.Utc(cancelledAtUtc);
            Status = PrescriptionStatus.Cancelled;
        }

        private void EnsureDraft()
        {
            if (Status != PrescriptionStatus.Draft)
            {
                throw new DomainException($"Only a draft prescription can be changed; current status is {Status}.");
            }
        }
    }
}
