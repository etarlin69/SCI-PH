using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;

namespace SCI_PH.Domain.Entities.Patients
{
    public sealed class PatientSurgicalHistory
    {
        private PatientSurgicalHistory() { }

        internal PatientSurgicalHistory(Guid medicalHistoryId, string procedureName, DateOnly? performedOn, string? facility, string? notes)
        {
            Id = Guid.NewGuid();
            MedicalHistoryId = medicalHistoryId;
            ProcedureName = Guard.Text(procedureName, FieldLengths.Name, nameof(procedureName));
            PerformedOn = performedOn;
            Facility = Guard.OptionalText(facility, FieldLengths.Name, nameof(facility));
            Notes = Guard.OptionalText(notes, FieldLengths.LongText, nameof(notes));
        }

        public Guid Id { get; private set; }
        public Guid MedicalHistoryId { get; private set; }
        public string ProcedureName { get; private set; } = string.Empty;
        public DateOnly? PerformedOn { get; private set; }
        public string? Facility { get; private set; }
        public string? Notes { get; private set; }
    }
}
