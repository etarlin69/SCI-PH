using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Domain.Entities.Patients
{
    public sealed class PatientAllergy
    {
        private PatientAllergy() { }

        internal PatientAllergy(
            Guid medicalHistoryId,
            string substance,
            AllergySeverity severity,
            ClinicalRecordStatus status,
            string? reaction,
            DateOnly? onsetDate,
            string? notes)
        {
            Id = Guid.NewGuid();
            MedicalHistoryId = medicalHistoryId;
            Substance = Guard.Text(substance, FieldLengths.Name, nameof(substance));
            Severity = Guard.Defined(severity, nameof(severity));
            Status = Guard.Defined(status, nameof(status));
            Reaction = Guard.OptionalText(reaction, FieldLengths.ShortText, nameof(reaction));
            OnsetDate = onsetDate;
            Notes = Guard.OptionalText(notes, FieldLengths.LongText, nameof(notes));
        }

        public Guid Id { get; private set; }
        public Guid MedicalHistoryId { get; private set; }
        public string Substance { get; private set; } = string.Empty;
        public AllergySeverity Severity { get; private set; }
        public ClinicalRecordStatus Status { get; private set; }
        public string? Reaction { get; private set; }
        public DateOnly? OnsetDate { get; private set; }
        public string? Notes { get; private set; }
    }
}
