using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Entities.Encounters
{
    // Procedure or intervention: Planned -> InProgress -> Completed, or Cancelled before completion.
    public sealed class ClinicalProcedure
    {
        private ClinicalProcedure() { }

        public Guid Id { get; private set; }
        public Guid ClinicalEncounterId { get; private set; }
        public ClinicalEncounter Encounter { get; private set; } = null!;
        public string Name { get; private set; } = string.Empty;
        public string? Description { get; private set; }
        public string? CodeSystem { get; private set; }
        public string? Code { get; private set; }
        public ClinicalProcedureStatus Status { get; private set; }
        public Guid? PerformedByStaffId { get; private set; }
        public DateTimeOffset? PerformedAtUtc { get; private set; }
        public DateTimeOffset? CompletedAtUtc { get; private set; }
        public string? Outcome { get; private set; }

        public static ClinicalProcedure Plan(
            Guid clinicalEncounterId,
            string name,
            string? description = null,
            string? codeSystem = null,
            string? code = null)
        {
            var (system, value) = ClinicalCode.Optional(codeSystem, code);
            return new ClinicalProcedure
            {
                Id = Guid.NewGuid(),
                ClinicalEncounterId = Guard.Id(clinicalEncounterId, nameof(clinicalEncounterId)),
                Name = Guard.Text(name, FieldLengths.Name, nameof(name)),
                Description = Guard.OptionalText(description, FieldLengths.LongText, nameof(description)),
                CodeSystem = system,
                Code = value,
                Status = ClinicalProcedureStatus.Planned
            };
        }

        // For procedures documented after they were done, which is common in emergency care.
        public static ClinicalProcedure RecordCompleted(
            Guid clinicalEncounterId,
            string name,
            Guid performedByStaffId,
            DateTimeOffset performedAtUtc,
            DateTimeOffset completedAtUtc,
            string? outcome = null,
            string? description = null,
            string? codeSystem = null,
            string? code = null)
        {
            var procedure = Plan(clinicalEncounterId, name, description, codeSystem, code);
            procedure.Start(performedByStaffId, performedAtUtc);
            procedure.Complete(completedAtUtc, outcome);
            return procedure;
        }

        public void Start(Guid performedByStaffId, DateTimeOffset performedAtUtc)
        {
            EnsureStatus(ClinicalProcedureStatus.Planned);
            PerformedByStaffId = Guard.Id(performedByStaffId, nameof(performedByStaffId));
            PerformedAtUtc = Guard.Utc(performedAtUtc);
            Status = ClinicalProcedureStatus.InProgress;
        }

        public void Complete(DateTimeOffset completedAtUtc, string? outcome = null)
        {
            EnsureStatus(ClinicalProcedureStatus.InProgress);
            CompletedAtUtc = Guard.NotBefore(completedAtUtc, PerformedAtUtc!.Value, nameof(completedAtUtc));
            Outcome = Guard.OptionalText(outcome, FieldLengths.LongText, nameof(outcome));
            Status = ClinicalProcedureStatus.Completed;
        }

        public void Cancel()
        {
            if (Status is ClinicalProcedureStatus.Completed or ClinicalProcedureStatus.Cancelled)
            {
                throw new DomainException($"A {Status} procedure cannot be cancelled.");
            }

            Status = ClinicalProcedureStatus.Cancelled;
        }

        private void EnsureStatus(ClinicalProcedureStatus expected)
        {
            if (Status != expected)
            {
                throw new DomainException($"The procedure must be {expected}; current status is {Status}.");
            }
        }
    }
}
