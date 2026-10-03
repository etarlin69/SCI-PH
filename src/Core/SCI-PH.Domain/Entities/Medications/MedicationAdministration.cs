using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Domain.Entities.Medications
{
    // A dose actually given during an encounter (also valid for NN patients).
    // Not the patient's usual medication: see HomeMedication.
    public sealed class MedicationAdministration
    {
        private MedicationAdministration() { }

        public Guid Id { get; private set; }
        public Guid ClinicalEncounterId { get; private set; }
        public Guid MedicationId { get; private set; }
        public Medication Medication { get; private set; } = null!;
        // Optional order being fulfilled; composite keys keep it inside the same encounter and prescription.
        public Guid? PrescriptionId { get; private set; }
        public Guid? PrescriptionDetailId { get; private set; }
        public decimal Dose { get; private set; }
        public string DoseUnit { get; private set; } = string.Empty;
        public MedicationRoute Route { get; private set; }
        public DateTimeOffset AdministeredAtUtc { get; private set; }
        public Guid AdministeredByStaffId { get; private set; }
        public MedicationAdministrationStatus Status { get; private set; }
        public string? Notes { get; private set; }

        public static MedicationAdministration Record(
            Guid clinicalEncounterId,
            Guid medicationId,
            decimal dose,
            string doseUnit,
            MedicationRoute route,
            DateTimeOffset administeredAtUtc,
            Guid administeredByStaffId,
            Guid? prescriptionId = null,
            Guid? prescriptionDetailId = null,
            string? notes = null)
        {
            if (prescriptionDetailId is not null && prescriptionId is null)
            {
                throw new ArgumentException("A prescription detail requires its prescription.", nameof(prescriptionId));
            }

            return new MedicationAdministration
            {
                Id = Guid.NewGuid(),
                ClinicalEncounterId = Guard.Id(clinicalEncounterId, nameof(clinicalEncounterId)),
                MedicationId = Guard.Id(medicationId, nameof(medicationId)),
                Dose = Guard.Positive(dose, nameof(dose)),
                DoseUnit = Guard.Text(doseUnit, FieldLengths.MeasurementUnit, nameof(doseUnit)),
                Route = Guard.Defined(route, nameof(route)),
                AdministeredAtUtc = Guard.Utc(administeredAtUtc),
                AdministeredByStaffId = Guard.Id(administeredByStaffId, nameof(administeredByStaffId)),
                PrescriptionId = Guard.OptionalId(prescriptionId, nameof(prescriptionId)),
                PrescriptionDetailId = Guard.OptionalId(prescriptionDetailId, nameof(prescriptionDetailId)),
                Notes = Guard.OptionalText(notes, FieldLengths.LongText, nameof(notes)),
                Status = MedicationAdministrationStatus.Completed
            };
        }

        // The row is kept for traceability; it is never deleted.
        public void MarkEnteredInError(string reason)
        {
            if (Status == MedicationAdministrationStatus.EnteredInError)
            {
                throw new DomainException("The administration is already marked as entered in error.");
            }

            Notes = Guard.Text(reason, FieldLengths.LongText, nameof(reason));
            Status = MedicationAdministrationStatus.EnteredInError;
        }
    }
}
