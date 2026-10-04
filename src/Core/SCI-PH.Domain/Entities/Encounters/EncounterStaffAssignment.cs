using SCI_PH.Domain.Common;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Entities.Encounters
{
    // Who took part in an encounter and in which function. Ended assignments stay as history,
    // so the same person can be assigned again later.
    public sealed class EncounterStaffAssignment
    {
        private EncounterStaffAssignment() { }

        public Guid Id { get; private set; }
        public Guid ClinicalEncounterId { get; private set; }
        public Guid ClinicalStaffId { get; private set; }
        public EncounterStaffFunction Function { get; private set; }
        public DateTimeOffset AssignedAtUtc { get; private set; }
        public DateTimeOffset? UnassignedAtUtc { get; private set; }

        public bool IsActive => UnassignedAtUtc is null;

        public static EncounterStaffAssignment Assign(
            Guid clinicalEncounterId,
            Guid clinicalStaffId,
            EncounterStaffFunction function,
            DateTimeOffset assignedAtUtc) =>
            new()
            {
                Id = Guid.NewGuid(),
                ClinicalEncounterId = Guard.Id(clinicalEncounterId, nameof(clinicalEncounterId)),
                ClinicalStaffId = Guard.Id(clinicalStaffId, nameof(clinicalStaffId)),
                Function = Guard.Defined(function, nameof(function)),
                AssignedAtUtc = Guard.Utc(assignedAtUtc)
            };

        public void Unassign(DateTimeOffset unassignedAtUtc)
        {
            if (!IsActive)
            {
                throw new DomainException("The assignment has already ended.");
            }

            UnassignedAtUtc = Guard.NotBefore(unassignedAtUtc, AssignedAtUtc, nameof(unassignedAtUtc));
        }
    }
}
