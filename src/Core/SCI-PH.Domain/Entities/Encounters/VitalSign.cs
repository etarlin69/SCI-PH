using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Entities.Encounters
{
    // A single measurement. Each new reading is a new row, so the encounter keeps the full history.
    public sealed class VitalSign
    {
        private VitalSign() { }

        public Guid Id { get; private set; }
        public Guid ClinicalEncounterId { get; private set; }
        public ClinicalEncounter Encounter { get; private set; } = null!;
        // Optional triage of the same encounter (enforced by a composite foreign key).
        public Guid? TriageId { get; private set; }
        public VitalSignType Type { get; private set; }
        public decimal Value { get; private set; }
        public string Unit { get; private set; } = string.Empty;
        public DateTimeOffset MeasuredAtUtc { get; private set; }
        // Null when the value comes from a device without a known operator.
        public Guid? MeasuredByStaffId { get; private set; }

        public static VitalSign Record(
            Guid clinicalEncounterId,
            VitalSignType type,
            decimal value,
            DateTimeOffset measuredAtUtc,
            Guid? measuredByStaffId = null,
            Guid? triageId = null)
        {
            var limit = VitalSignLimits.For(Guard.Defined(type, nameof(type)));
            if (value < limit.Minimum || value > limit.Maximum)
            {
                throw new DomainException($"{type} must be between {limit.Minimum} and {limit.Maximum} {limit.Unit}.");
            }

            return new VitalSign
            {
                Id = Guid.NewGuid(),
                ClinicalEncounterId = Guard.Id(clinicalEncounterId, nameof(clinicalEncounterId)),
                TriageId = Guard.OptionalId(triageId, nameof(triageId)),
                Type = type,
                Value = value,
                Unit = limit.Unit,
                MeasuredAtUtc = Guard.Utc(measuredAtUtc),
                MeasuredByStaffId = Guard.OptionalId(measuredByStaffId, nameof(measuredByStaffId))
            };
        }
    }
}
