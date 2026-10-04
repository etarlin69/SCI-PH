using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Domain.Entities.Patients
{
    // Medication the patient usually takes at home. It is history, not a dose given during care
    // (see MedicationAdministration).
    public sealed class HomeMedication
    {
        private HomeMedication() { }

        internal HomeMedication(
            Guid medicalHistoryId,
            string medicationName,
            HomeMedicationStatus status,
            Guid? medicationId,
            string? doseInstructions,
            string? frequency,
            MedicationRoute? route,
            DateOnly? startedOn,
            DateOnly? stoppedOn,
            string? notes)
        {
            if (stoppedOn < startedOn)
            {
                throw new ArgumentOutOfRangeException(nameof(stoppedOn), stoppedOn, "A medication cannot stop before it started.");
            }

            Id = Guid.NewGuid();
            MedicalHistoryId = medicalHistoryId;
            MedicationName = Guard.Text(medicationName, FieldLengths.Name, nameof(medicationName));
            Status = Guard.Defined(status, nameof(status));
            MedicationId = Guard.OptionalId(medicationId, nameof(medicationId));
            DoseInstructions = Guard.OptionalText(doseInstructions, FieldLengths.ShortText, nameof(doseInstructions));
            Frequency = Guard.OptionalText(frequency, FieldLengths.Frequency, nameof(frequency));
            Route = Guard.OptionalDefined(route, nameof(route));
            StartedOn = startedOn;
            StoppedOn = stoppedOn;
            Notes = Guard.OptionalText(notes, FieldLengths.LongText, nameof(notes));
        }

        public Guid Id { get; private set; }
        public Guid MedicalHistoryId { get; private set; }
        // Optional catalog link; the free-text name keeps what the patient reported.
        public Guid? MedicationId { get; private set; }
        public string MedicationName { get; private set; } = string.Empty;
        public string? DoseInstructions { get; private set; }
        public string? Frequency { get; private set; }
        public MedicationRoute? Route { get; private set; }
        public HomeMedicationStatus Status { get; private set; }
        public DateOnly? StartedOn { get; private set; }
        public DateOnly? StoppedOn { get; private set; }
        public string? Notes { get; private set; }
    }
}
