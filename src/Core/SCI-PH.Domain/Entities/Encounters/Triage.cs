using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Encounters;

namespace SCI_PH.Domain.Entities.Encounters
{
    // One triage assessment. An encounter can be re-triaged many times; the latest one is current.
    public sealed class Triage
    {
        private Triage() { }

        public Guid Id { get; private set; }
        public Guid ClinicalEncounterId { get; private set; }
        public ClinicalEncounter Encounter { get; private set; } = null!;
        public TriagePriority Priority { get; private set; }
        public string? Reason { get; private set; }
        public string? Observations { get; private set; }
        public DateTimeOffset PerformedAtUtc { get; private set; }
        public Guid PerformedByStaffId { get; private set; }

        public static Triage Record(
            Guid clinicalEncounterId,
            TriagePriority priority,
            Guid performedByStaffId,
            DateTimeOffset performedAtUtc,
            string? reason = null,
            string? observations = null) =>
            new()
            {
                Id = Guid.NewGuid(),
                ClinicalEncounterId = Guard.Id(clinicalEncounterId, nameof(clinicalEncounterId)),
                Priority = Guard.Defined(priority, nameof(priority)),
                PerformedByStaffId = Guard.Id(performedByStaffId, nameof(performedByStaffId)),
                PerformedAtUtc = Guard.Utc(performedAtUtc),
                Reason = Guard.OptionalText(reason, FieldLengths.ShortText, nameof(reason)),
                Observations = Guard.OptionalText(observations, FieldLengths.LongText, nameof(observations))
            };
    }
}
