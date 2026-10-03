using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Medications;

namespace SCI_PH.Domain.Entities.Medications
{
    public sealed class PrescriptionDetail
    {
        private PrescriptionDetail() { }

        internal PrescriptionDetail(
            Guid prescriptionId,
            Guid medicationId,
            decimal dose,
            string doseUnit,
            MedicationRoute route,
            string frequency,
            int? durationDays,
            string? instructions)
        {
            if (durationDays <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationDays), durationDays, "Duration must be at least one day.");
            }

            Id = Guid.NewGuid();
            PrescriptionId = prescriptionId;
            MedicationId = Guard.Id(medicationId, nameof(medicationId));
            Dose = Guard.Positive(dose, nameof(dose));
            DoseUnit = Guard.Text(doseUnit, FieldLengths.MeasurementUnit, nameof(doseUnit));
            Route = Guard.Defined(route, nameof(route));
            Frequency = Guard.Text(frequency, FieldLengths.Frequency, nameof(frequency));
            DurationDays = durationDays;
            Instructions = Guard.OptionalText(instructions, FieldLengths.LongText, nameof(instructions));
        }

        public Guid Id { get; private set; }
        public Guid PrescriptionId { get; private set; }
        public Guid MedicationId { get; private set; }
        public Medication Medication { get; private set; } = null!;
        public decimal Dose { get; private set; }
        public string DoseUnit { get; private set; } = string.Empty;
        public MedicationRoute Route { get; private set; }
        public string Frequency { get; private set; } = string.Empty;
        public int? DurationDays { get; private set; }
        public string? Instructions { get; private set; }
    }
}
