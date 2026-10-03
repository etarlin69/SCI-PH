using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Domain.Entities.Patients
{
    // Versioned snapshot of the structured history. A new version is recorded instead of editing a previous one;
    // Application assigns the next Version per patient and the unique index rejects concurrent duplicates.
    public sealed class PatientMedicalHistory
    {
        private readonly List<PatientAllergy> _allergies = [];
        private readonly List<ChronicCondition> _chronicConditions = [];
        private readonly List<HomeMedication> _homeMedications = [];
        private readonly List<PatientSurgicalHistory> _surgicalHistories = [];

        private PatientMedicalHistory() { }

        public Guid Id { get; private set; }
        public Guid PatientId { get; private set; }
        public Patient Patient { get; private set; } = null!;
        public int Version { get; private set; }
        public DateTimeOffset RecordedAtUtc { get; private set; }
        public Guid? RecordedByStaffId { get; private set; }
        public string? Notes { get; private set; }

        public IReadOnlyCollection<PatientAllergy> Allergies => _allergies.AsReadOnly();
        public IReadOnlyCollection<ChronicCondition> ChronicConditions => _chronicConditions.AsReadOnly();
        public IReadOnlyCollection<HomeMedication> HomeMedications => _homeMedications.AsReadOnly();
        public IReadOnlyCollection<PatientSurgicalHistory> SurgicalHistories => _surgicalHistories.AsReadOnly();

        public static PatientMedicalHistory Record(Guid patientId, int version, Guid? recordedByStaffId = null, string? notes = null) =>
            new()
            {
                Id = Guid.NewGuid(),
                PatientId = Guard.Id(patientId, nameof(patientId)),
                Version = version >= 1 ? version : throw new ArgumentOutOfRangeException(nameof(version), version, "Versions start at 1."),
                RecordedAtUtc = DateTimeOffset.UtcNow,
                RecordedByStaffId = Guard.OptionalId(recordedByStaffId, nameof(recordedByStaffId)),
                Notes = Guard.OptionalText(notes, FieldLengths.LongText, nameof(notes))
            };

        public PatientAllergy AddAllergy(
            string substance,
            AllergySeverity severity,
            ClinicalRecordStatus status,
            string? reaction = null,
            DateOnly? onsetDate = null,
            string? notes = null)
        {
            var allergy = new PatientAllergy(Id, substance, severity, status, reaction, onsetDate, notes);
            _allergies.Add(allergy);
            return allergy;
        }

        public ChronicCondition AddChronicCondition(
            string name,
            ClinicalRecordStatus status,
            string? codeSystem = null,
            string? code = null,
            DateOnly? diagnosedOn = null,
            string? notes = null)
        {
            var condition = new ChronicCondition(Id, name, status, codeSystem, code, diagnosedOn, notes);
            _chronicConditions.Add(condition);
            return condition;
        }

        public HomeMedication AddHomeMedication(
            string medicationName,
            HomeMedicationStatus status,
            Guid? medicationId = null,
            string? doseInstructions = null,
            string? frequency = null,
            MedicationRoute? route = null,
            DateOnly? startedOn = null,
            DateOnly? stoppedOn = null,
            string? notes = null)
        {
            var medication = new HomeMedication(
                Id, medicationName, status, medicationId, doseInstructions, frequency, route, startedOn, stoppedOn, notes);
            _homeMedications.Add(medication);
            return medication;
        }

        public PatientSurgicalHistory AddSurgicalHistory(
            string procedureName,
            DateOnly? performedOn = null,
            string? facility = null,
            string? notes = null)
        {
            var surgery = new PatientSurgicalHistory(Id, procedureName, performedOn, facility, notes);
            _surgicalHistories.Add(surgery);
            return surgery;
        }
    }
}
