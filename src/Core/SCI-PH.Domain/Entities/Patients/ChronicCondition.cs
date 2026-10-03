using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Domain.Entities.Patients
{
    public sealed class ChronicCondition
    {
        private ChronicCondition() { }

        internal ChronicCondition(
            Guid medicalHistoryId,
            string name,
            ClinicalRecordStatus status,
            string? codeSystem,
            string? code,
            DateOnly? diagnosedOn,
            string? notes)
        {
            (CodeSystem, Code) = ClinicalCode.Optional(codeSystem, code);
            Id = Guid.NewGuid();
            MedicalHistoryId = medicalHistoryId;
            Name = Guard.Text(name, FieldLengths.Name, nameof(name));
            Status = Guard.Defined(status, nameof(status));
            DiagnosedOn = diagnosedOn;
            Notes = Guard.OptionalText(notes, FieldLengths.LongText, nameof(notes));
        }

        public Guid Id { get; private set; }
        public Guid MedicalHistoryId { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public string? CodeSystem { get; private set; }
        public string? Code { get; private set; }
        public ClinicalRecordStatus Status { get; private set; }
        public DateOnly? DiagnosedOn { get; private set; }
        public string? Notes { get; private set; }
    }
}
