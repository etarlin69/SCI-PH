using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Documents;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Organizations;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Entities.Transport;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Exceptions;
using SCI_PH.Domain.ValueObjects;

namespace SCI_PH.Domain.Entities.Encounters
{
    // A consultation, emergency, prehospital care or hospital stay.
    // Clinical records reference the encounter, so identifying an NN patient never moves them.
    public sealed class ClinicalEncounter
    {
        public static IReadOnlyCollection<ClinicalEncounterType> TypesAllowingUnidentifiedPatient { get; } =
            [ClinicalEncounterType.Emergency, ClinicalEncounterType.Prehospital];

        public static IReadOnlyCollection<ClinicalEncounterStatus> FinalStatuses { get; } =
            [ClinicalEncounterStatus.Closed, ClinicalEncounterStatus.Cancelled];

        private static readonly IReadOnlyDictionary<ClinicalEncounterStatus, ClinicalEncounterStatus[]> _allowedTransitions =
            new Dictionary<ClinicalEncounterStatus, ClinicalEncounterStatus[]>
            {
                [ClinicalEncounterStatus.Created] = [ClinicalEncounterStatus.InProgress, ClinicalEncounterStatus.Cancelled],
                [ClinicalEncounterStatus.InProgress] =
                [
                    ClinicalEncounterStatus.Transferred,
                    ClinicalEncounterStatus.InEvolution,
                    ClinicalEncounterStatus.Closed,
                    ClinicalEncounterStatus.Cancelled
                ],
                // A cancelled transfer returns the encounter to InProgress.
                [ClinicalEncounterStatus.Transferred] = [ClinicalEncounterStatus.Received, ClinicalEncounterStatus.InProgress],
                [ClinicalEncounterStatus.Received] =
                    [ClinicalEncounterStatus.InProgress, ClinicalEncounterStatus.InEvolution, ClinicalEncounterStatus.Closed],
                [ClinicalEncounterStatus.InEvolution] = [ClinicalEncounterStatus.Transferred, ClinicalEncounterStatus.Closed],
                [ClinicalEncounterStatus.Closed] = [],
                [ClinicalEncounterStatus.Cancelled] = []
            };

        private readonly List<EncounterStaffAssignment> _staffAssignments = [];
        private readonly List<Triage> _triages = [];
        private readonly List<VitalSign> _vitalSigns = [];
        private readonly List<ClinicalNote> _notes = [];
        private readonly List<Diagnosis> _diagnoses = [];
        private readonly List<ClinicalProcedure> _procedures = [];
        private readonly List<ClinicalResult> _results = [];
        private readonly List<ClinicalDocument> _documents = [];
        private readonly List<Prescription> _prescriptions = [];
        private readonly List<MedicationAdministration> _medicationAdministrations = [];
        private readonly List<ClinicalTransfer> _transfers = [];

        private ClinicalEncounter() { }

        public Guid Id { get; private set; }
        public Guid? PatientId { get; private set; }
        public Patient? Patient { get; private set; }
        // Set only for encounters that started as NN; kept after identification as a historical reference.
        public TemporaryPatientCode? TemporaryPatientCode { get; private set; }
        public DateTimeOffset? IdentifiedAtUtc { get; private set; }
        public Guid? IdentifiedByStaffId { get; private set; }
        // Optional only for prehospital care, which can start before a clinic is involved.
        public Guid? ClinicId { get; private set; }
        public Clinic? Clinic { get; private set; }
        public Guid? AmbulanceId { get; private set; }
        public Ambulance? Ambulance { get; private set; }
        public ClinicalEncounterType Type { get; private set; }
        public ClinicalEncounterStatus Status { get; private set; }
        public string? Reason { get; private set; }
        public DateTimeOffset StartedAtUtc { get; private set; }
        public DateTimeOffset? EndedAtUtc { get; private set; }
        public DateTimeOffset CreatedAtUtc { get; private set; }
        public DateTimeOffset? UpdatedAtUtc { get; private set; }

        public bool IsPatientIdentified => PatientId.HasValue;
        public bool StartedAsUnidentified => TemporaryPatientCode is not null;

        public IReadOnlyCollection<EncounterStaffAssignment> StaffAssignments => _staffAssignments.AsReadOnly();
        public IReadOnlyCollection<Triage> Triages => _triages.AsReadOnly();
        public IReadOnlyCollection<VitalSign> VitalSigns => _vitalSigns.AsReadOnly();
        public IReadOnlyCollection<ClinicalNote> Notes => _notes.AsReadOnly();
        public IReadOnlyCollection<Diagnosis> Diagnoses => _diagnoses.AsReadOnly();
        public IReadOnlyCollection<ClinicalProcedure> Procedures => _procedures.AsReadOnly();
        public IReadOnlyCollection<ClinicalResult> Results => _results.AsReadOnly();
        public IReadOnlyCollection<ClinicalDocument> Documents => _documents.AsReadOnly();
        public IReadOnlyCollection<Prescription> Prescriptions => _prescriptions.AsReadOnly();
        public IReadOnlyCollection<MedicationAdministration> MedicationAdministrations => _medicationAdministrations.AsReadOnly();
        public IReadOnlyCollection<ClinicalTransfer> Transfers => _transfers.AsReadOnly();

        public static ClinicalEncounter CreateForPatient(
            Guid patientId,
            ClinicalEncounterType type,
            DateTimeOffset startedAtUtc,
            Guid? clinicId = null,
            Guid? ambulanceId = null,
            string? reason = null)
        {
            var encounter = Create(type, startedAtUtc, clinicId, ambulanceId, reason);
            encounter.PatientId = Guard.Id(patientId, nameof(patientId));
            return encounter;
        }

        public static ClinicalEncounter CreateUnidentified(
            ClinicalEncounterType type,
            DateTimeOffset startedAtUtc,
            Guid? clinicId = null,
            Guid? ambulanceId = null,
            string? reason = null)
        {
            if (!TypesAllowingUnidentifiedPatient.Contains(type))
            {
                throw new InvalidClinicalEncounterStateException($"A {type} encounter requires an identified patient.");
            }

            var encounter = Create(type, startedAtUtc, clinicId, ambulanceId, reason);
            encounter.TemporaryPatientCode = TemporaryPatientCode.New();
            return encounter;
        }

        // Links the same encounter to the real patient. Save it together with its ClinicalAuditEntry.
        public void IdentifyPatient(Guid patientId, Guid identifiedByStaffId, DateTimeOffset identifiedAtUtc)
        {
            if (IsPatientIdentified)
            {
                throw new PatientAlreadyIdentifiedException(Id);
            }

            if (Status == ClinicalEncounterStatus.Cancelled)
            {
                throw new InvalidClinicalEncounterStateException("A cancelled encounter cannot be identified.");
            }

            var patient = Guard.Id(patientId, nameof(patientId));
            var staff = Guard.Id(identifiedByStaffId, nameof(identifiedByStaffId));
            IdentifiedAtUtc = Guard.NotBefore(identifiedAtUtc, StartedAtUtc, nameof(identifiedAtUtc));
            PatientId = patient;
            IdentifiedByStaffId = staff;
            Touch();
        }

        public bool CanChangeStatusTo(ClinicalEncounterStatus newStatus) => _allowedTransitions[Status].Contains(newStatus);

        public void ChangeStatus(ClinicalEncounterStatus newStatus, DateTimeOffset changedAtUtc)
        {
            Guard.Defined(newStatus, nameof(newStatus));
            if (!CanChangeStatusTo(newStatus))
            {
                throw new InvalidClinicalEncounterStateException($"An encounter cannot change from {Status} to {newStatus}.");
            }

            var changedAt = Guard.NotBefore(changedAtUtc, StartedAtUtc, nameof(changedAtUtc));
            if (FinalStatuses.Contains(newStatus))
            {
                EndedAtUtc = changedAt;
            }

            Status = newStatus;
            Touch();
        }

        private static ClinicalEncounter Create(
            ClinicalEncounterType type,
            DateTimeOffset startedAtUtc,
            Guid? clinicId,
            Guid? ambulanceId,
            string? reason)
        {
            Guard.Defined(type, nameof(type));
            if (clinicId is null && type != ClinicalEncounterType.Prehospital)
            {
                throw new InvalidClinicalEncounterStateException($"A {type} encounter requires a clinic.");
            }

            return new ClinicalEncounter
            {
                Id = Guid.NewGuid(),
                Type = type,
                Status = ClinicalEncounterStatus.Created,
                StartedAtUtc = Guard.Utc(startedAtUtc),
                ClinicId = Guard.OptionalId(clinicId, nameof(clinicId)),
                AmbulanceId = Guard.OptionalId(ambulanceId, nameof(ambulanceId)),
                Reason = Guard.OptionalText(reason, FieldLengths.LongText, nameof(reason)),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
        }

        private void Touch() => UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
