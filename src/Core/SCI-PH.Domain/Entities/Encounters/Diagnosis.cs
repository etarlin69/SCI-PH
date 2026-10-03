using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Entities.Encounters
{
    // The code is optional: the academic prototype does not require a full ICD catalog.
    public sealed class Diagnosis
    {
        private Diagnosis() { }

        public Guid Id { get; private set; }
        public Guid ClinicalEncounterId { get; private set; }
        public ClinicalEncounter Encounter { get; private set; } = null!;
        public Guid RecordedByStaffId { get; private set; }
        public string Description { get; private set; } = string.Empty;
        public string? CodeSystem { get; private set; }
        public string? Code { get; private set; }
        public DiagnosisType Type { get; private set; }
        public bool IsPrimary { get; private set; }
        public DateTimeOffset RecordedAtUtc { get; private set; }
        public DateTimeOffset? ConfirmedAtUtc { get; private set; }

        public static Diagnosis Record(
            Guid clinicalEncounterId,
            Guid recordedByStaffId,
            string description,
            DiagnosisType type,
            DateTimeOffset recordedAtUtc,
            bool isPrimary = false,
            string? codeSystem = null,
            string? code = null)
        {
            var (system, value) = ClinicalCode.Optional(codeSystem, code);
            var recordedAt = Guard.Utc(recordedAtUtc);
            var diagnosisType = Guard.Defined(type, nameof(type));
            return new Diagnosis
            {
                Id = Guid.NewGuid(),
                ClinicalEncounterId = Guard.Id(clinicalEncounterId, nameof(clinicalEncounterId)),
                RecordedByStaffId = Guard.Id(recordedByStaffId, nameof(recordedByStaffId)),
                Description = Guard.Text(description, FieldLengths.ShortText, nameof(description)),
                CodeSystem = system,
                Code = value,
                Type = diagnosisType,
                IsPrimary = isPrimary,
                RecordedAtUtc = recordedAt,
                ConfirmedAtUtc = diagnosisType == DiagnosisType.Definitive ? recordedAt : null
            };
        }

        public void ConfirmAsDefinitive(DateTimeOffset confirmedAtUtc)
        {
            if (Type == DiagnosisType.Definitive)
            {
                throw new DomainException("The diagnosis is already definitive.");
            }

            ConfirmedAtUtc = Guard.NotBefore(confirmedAtUtc, RecordedAtUtc, nameof(confirmedAtUtc));
            Type = DiagnosisType.Definitive;
        }
    }
}
