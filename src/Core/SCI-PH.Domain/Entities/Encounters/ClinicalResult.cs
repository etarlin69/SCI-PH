using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Entities.Encounters
{
    // Result of a test or procedure. When linked to a procedure, both belong to the same encounter
    // (enforced by a composite foreign key).
    public sealed class ClinicalResult
    {
        private ClinicalResult() { }

        public Guid Id { get; private set; }
        public Guid ClinicalEncounterId { get; private set; }
        public ClinicalEncounter Encounter { get; private set; } = null!;
        public Guid? ClinicalProcedureId { get; private set; }
        public ClinicalResultType Type { get; private set; }
        public ClinicalResultStatus Status { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public string? CodeSystem { get; private set; }
        public string? Code { get; private set; }
        public string? ResultText { get; private set; }
        public decimal? NumericValue { get; private set; }
        public string? Unit { get; private set; }
        public string? ReferenceRange { get; private set; }
        public DateTimeOffset? ResultedAtUtc { get; private set; }
        public Guid? RecordedByStaffId { get; private set; }

        public static ClinicalResult Request(
            Guid clinicalEncounterId,
            ClinicalResultType type,
            string name,
            Guid? clinicalProcedureId = null,
            string? codeSystem = null,
            string? code = null,
            string? referenceRange = null)
        {
            var (system, value) = ClinicalCode.Optional(codeSystem, code);
            return new ClinicalResult
            {
                Id = Guid.NewGuid(),
                ClinicalEncounterId = Guard.Id(clinicalEncounterId, nameof(clinicalEncounterId)),
                ClinicalProcedureId = Guard.OptionalId(clinicalProcedureId, nameof(clinicalProcedureId)),
                Type = Guard.Defined(type, nameof(type)),
                Name = Guard.Text(name, FieldLengths.Name, nameof(name)),
                CodeSystem = system,
                Code = value,
                ReferenceRange = Guard.OptionalText(referenceRange, FieldLengths.ReferenceRange, nameof(referenceRange)),
                Status = ClinicalResultStatus.Pending
            };
        }

        public void RecordFinal(DateTimeOffset resultedAtUtc, string? resultText = null, decimal? numericValue = null, string? unit = null, Guid? recordedByStaffId = null)
        {
            EnsureStatus(ClinicalResultStatus.Pending);
            SetValue(resultedAtUtc, resultText, numericValue, unit, recordedByStaffId);
            Status = ClinicalResultStatus.Final;
        }

        public void Correct(DateTimeOffset resultedAtUtc, string? resultText = null, decimal? numericValue = null, string? unit = null, Guid? recordedByStaffId = null)
        {
            if (Status is not (ClinicalResultStatus.Final or ClinicalResultStatus.Corrected))
            {
                throw new DomainException("Only a final result can be corrected.");
            }

            SetValue(resultedAtUtc, resultText, numericValue, unit, recordedByStaffId);
            Status = ClinicalResultStatus.Corrected;
        }

        public void Cancel()
        {
            EnsureStatus(ClinicalResultStatus.Pending);
            Status = ClinicalResultStatus.Cancelled;
        }

        private void SetValue(DateTimeOffset resultedAtUtc, string? resultText, decimal? numericValue, string? unit, Guid? recordedByStaffId)
        {
            var text = Guard.OptionalText(resultText, FieldLengths.LongText, nameof(resultText));
            if (text is null && numericValue is null)
            {
                throw new DomainException("A result needs a text or numeric value.");
            }

            ResultText = text;
            NumericValue = numericValue;
            Unit = Guard.OptionalText(unit, FieldLengths.MeasurementUnit, nameof(unit));
            ResultedAtUtc = Guard.Utc(resultedAtUtc);
            RecordedByStaffId = Guard.OptionalId(recordedByStaffId, nameof(recordedByStaffId));
        }

        private void EnsureStatus(ClinicalResultStatus expected)
        {
            if (Status != expected)
            {
                throw new DomainException($"The result must be {expected}; current status is {Status}.");
            }
        }
    }
}
