using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Authorization;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Medications;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Entities.Transport;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.Enums.Transport;

namespace SCI_PH.Application.Tests.Clinical
{
    internal sealed class FakeCurrentUser(Guid? userId, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions) : ICurrentUser
    {
        public static FakeCurrentUser InRole(string role) => new(Guid.NewGuid(), [role], RolePermissions.For(role));

        public bool IsAuthenticated => userId.HasValue;
        public Guid? UserId => userId;
        public IReadOnlyCollection<string> Roles => roles;
        public IReadOnlyCollection<string> Permissions => permissions;
        public bool HasPermission(string permission) => IsAuthenticated && permissions.Contains(permission);
    }

    internal sealed class FakeStaffDirectory : IClinicalStaffDirectory
    {
        private readonly Dictionary<Guid, ClinicalStaffMembership> _byUser = [];

        public ClinicalStaffMembership Add(Guid userId, bool isActive = true, params Guid[] clinicIds) =>
            _byUser[userId] = new ClinicalStaffMembership(Guid.NewGuid(), isActive, clinicIds);

        public Task<ClinicalStaffMembership?> FindByIdentityUserAsync(Guid identityUserId, CancellationToken cancellationToken) =>
            Task.FromResult(_byUser.GetValueOrDefault(identityUserId));

        public Task<IReadOnlyList<ClinicSummary>> GetClinicsAsync(IReadOnlyCollection<Guid> clinicIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClinicSummary>>(clinicIds.Select(id => new ClinicSummary(id, $"Clinic {id:N}")).ToList());
    }

    internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    // In-memory clinical store: writes become visible only after SaveChangesAsync, like a transaction.
    internal sealed class FakeClinicalStore : IClinicalReadStore, IClinicalWriteStore
    {
        private readonly List<object> _pending = [];

        public List<Patient> Patients { get; } = [];
        public List<ClinicalEncounter> Encounters { get; } = [];
        public List<Triage> Triages { get; } = [];
        public List<VitalSign> VitalSigns { get; } = [];
        public List<ClinicalNote> Notes { get; } = [];
        public List<Diagnosis> Diagnoses { get; } = [];
        public List<Medication> Medications { get; } = [];
        public List<Prescription> Prescriptions { get; } = [];
        public List<MedicationAdministration> Administrations { get; } = [];
        public List<ClinicalTransfer> Transfers { get; } = [];
        // Clinics that exist, with their status. Unknown ids do not exist.
        public Dictionary<Guid, ClinicStatus> Clinics { get; } = [];
        public HashSet<Guid> GuardedEncounters { get; } = [];
        public List<ClinicalAuditEntry> Audit { get; } = [];
        public int SaveCount { get; private set; }

        public Task<bool> PatientExistsAsync(Guid patientId, CancellationToken cancellationToken) =>
            Task.FromResult(Patients.Any(p => p.Id == patientId));

        public Task<bool> ActiveIdentifierExistsAsync(PatientIdentifierType type, string issuer, string normalizedValue, CancellationToken cancellationToken) =>
            Task.FromResult(Patients.SelectMany(p => p.Identifiers)
                .Any(i => i.IsActive && i.Type == type && i.Issuer == issuer && i.NormalizedValue == normalizedValue));

        public Task<PatientRow?> GetPatientAsync(Guid patientId, CancellationToken cancellationToken) =>
            Task.FromResult(Patients.SingleOrDefault(p => p.Id == patientId)?.ToTestRow());

        public Task<IReadOnlyList<PatientRow>> FindPatientsByIdentifierAsync(
            PatientIdentifierType type, string normalizedValue, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PatientRow>>(Patients
                .Where(p => p.Identifiers.Any(i => i.IsActive && i.Type == type && i.NormalizedValue == normalizedValue))
                .Take(limit).Select(p => p.ToTestRow()).ToList());

        public Task<IReadOnlyList<PatientRow>> FindPatientsByNameAsync(string term, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PatientRow>>(Patients
                .Where(p => $"{p.GivenNames} {p.FamilyNames}".Contains(term, StringComparison.OrdinalIgnoreCase))
                .Take(limit).Select(p => p.ToTestRow()).ToList());

        public Task<EncounterRow?> GetEncounterAsync(Guid encounterId, CancellationToken cancellationToken) =>
            Task.FromResult(Encounters.SingleOrDefault(e => e.Id == encounterId) is { } e ? ToRow(e) : null);

        public Task<IReadOnlyList<EncounterRow>> ListEncountersAsync(
            Guid clinicId, IReadOnlyCollection<ClinicalEncounterStatus> excludedStatuses, bool onlyUnidentified, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EncounterRow>>(Encounters
                .Where(e => e.ClinicId == clinicId && !excludedStatuses.Contains(e.Status) && (!onlyUnidentified || e.PatientId is null))
                .Take(limit).Select(ToRow).ToList());

        public Task<IReadOnlyList<TriageSummary>> ListTriagesAsync(Guid encounterId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TriageSummary>>(Triages.Where(t => t.ClinicalEncounterId == encounterId)
                .OrderByDescending(t => t.PerformedAtUtc)
                .Select(t => new TriageSummary(t.Id, t.ClinicalEncounterId, t.Priority, t.Reason, t.Observations, t.PerformedAtUtc, t.PerformedByStaffId))
                .ToList());

        public Task<IReadOnlyList<VitalSignSummary>> ListVitalSignsAsync(Guid encounterId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VitalSignSummary>>(VitalSigns.Where(v => v.ClinicalEncounterId == encounterId)
                .Select(v => new VitalSignSummary(v.Id, v.ClinicalEncounterId, v.TriageId, v.Type, v.Value, v.Unit, v.MeasuredAtUtc, v.MeasuredByStaffId))
                .ToList());

        public Task<IReadOnlyList<ClinicalNoteSummary>> ListNotesAsync(Guid encounterId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClinicalNoteSummary>>(Notes.Where(n => n.ClinicalEncounterId == encounterId)
                .OrderByDescending(n => n.OccurredAtUtc)
                .Select(n => new ClinicalNoteSummary(n.Id, n.ClinicalEncounterId, n.Type, n.Content, n.OccurredAtUtc, n.CreatedAtUtc, n.AuthorStaffId, "Staff"))
                .ToList());

        public Task<IReadOnlyList<DiagnosisSummary>> ListDiagnosesAsync(Guid encounterId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DiagnosisSummary>>(Diagnoses.Where(d => d.ClinicalEncounterId == encounterId)
                .Select(d => new DiagnosisSummary(d.Id, d.ClinicalEncounterId, d.Description, d.CodeSystem, d.Code, d.Type, d.IsPrimary, d.RecordedAtUtc, d.ConfirmedAtUtc, d.RecordedByStaffId))
                .ToList());

        public Task<IReadOnlyList<MedicationSummary>> ListMedicationsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MedicationSummary>>(Medications.OrderBy(m => m.Name).Select(ToSummary).ToList());

        public Task<IReadOnlyList<MedicationSummary>> FindMedicationsAsync(IReadOnlyCollection<Guid> medicationIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MedicationSummary>>(Medications.Where(m => medicationIds.Contains(m.Id)).Select(ToSummary).ToList());

        public Task<IReadOnlyList<PrescriptionSummary>> ListPrescriptionsAsync(Guid encounterId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PrescriptionSummary>>(Prescriptions.Where(p => p.ClinicalEncounterId == encounterId)
                .OrderByDescending(p => p.IssuedAtUtc ?? p.CreatedAtUtc).Select(ToSummary).ToList());

        public Task<PrescriptionSummary?> GetPrescriptionAsync(Guid prescriptionId, CancellationToken cancellationToken) =>
            Task.FromResult(Prescriptions.SingleOrDefault(p => p.Id == prescriptionId) is { } p ? ToSummary(p) : null);

        public Task<IReadOnlyList<MedicationAdministrationSummary>> ListMedicationAdministrationsAsync(Guid encounterId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MedicationAdministrationSummary>>(Administrations.Where(a => a.ClinicalEncounterId == encounterId)
                .OrderByDescending(a => a.AdministeredAtUtc)
                .Select(a => new MedicationAdministrationSummary(
                    a.Id, a.ClinicalEncounterId, a.MedicationId, MedicationName(a.MedicationId), a.PrescriptionId, a.PrescriptionDetailId,
                    a.Dose, a.DoseUnit, a.Route, a.AdministeredAtUtc, a.AdministeredByStaffId, a.Status, a.Notes))
                .ToList());

        public Task<ClinicStatus?> GetClinicStatusAsync(Guid clinicId, CancellationToken cancellationToken) =>
            Task.FromResult(Clinics.TryGetValue(clinicId, out var status) ? status : (ClinicStatus?)null);

        public Task<bool> HasActiveTransferAsync(Guid encounterId, CancellationToken cancellationToken) =>
            Task.FromResult(Transfers.Any(t => t.ClinicalEncounterId == encounterId
                && ClinicalTransfer.ActiveStatuses.Contains(t.Status)));

        public Task<TransferRow?> GetTransferAsync(Guid transferId, CancellationToken cancellationToken) =>
            Task.FromResult(Transfers.SingleOrDefault(t => t.Id == transferId) is { } t ? ToRow(t) : null);

        public Task<IReadOnlyList<TransferRow>> ListTransfersToAsync(
            IReadOnlyCollection<Guid> destinationClinicIds, IReadOnlyCollection<ClinicalTransferStatus> statuses, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TransferRow>>(Transfers
                .Where(t => destinationClinicIds.Contains(t.DestinationClinicId) && statuses.Contains(t.Status))
                .OrderByDescending(t => t.RequestedAtUtc).Take(limit).Select(ToRow).ToList());

        public Task<ClinicalTransfer?> FindTransferAsync(Guid transferId, CancellationToken cancellationToken) =>
            Task.FromResult(Transfers.SingleOrDefault(t => t.Id == transferId));

        public void Add(ClinicalTransfer transfer) => _pending.Add(transfer);

        public Task<ClinicalEncounter?> FindEncounterAsync(Guid encounterId, CancellationToken cancellationToken) =>
            Task.FromResult(Encounters.SingleOrDefault(e => e.Id == encounterId));

        public void Add(Patient patient) => _pending.Add(patient);
        public void Add(ClinicalEncounter encounter) => _pending.Add(encounter);
        public void Add(Triage triage) => _pending.Add(triage);
        public void Add(VitalSign vitalSign) => _pending.Add(vitalSign);
        public void Add(ClinicalNote note) => _pending.Add(note);
        public void Add(Diagnosis diagnosis) => _pending.Add(diagnosis);
        public void Add(Prescription prescription) => _pending.Add(prescription);
        public void Add(MedicationAdministration administration) => _pending.Add(administration);
        public void Add(ClinicalAuditEntry auditEntry) => _pending.Add(auditEntry);
        public void EnsureEncounterUnchanged(ClinicalEncounter encounter) => GuardedEncounters.Add(encounter.Id);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Patients.AddRange(_pending.OfType<Patient>());
            Encounters.AddRange(_pending.OfType<ClinicalEncounter>());
            Triages.AddRange(_pending.OfType<Triage>());
            VitalSigns.AddRange(_pending.OfType<VitalSign>());
            Notes.AddRange(_pending.OfType<ClinicalNote>());
            Diagnoses.AddRange(_pending.OfType<Diagnosis>());
            Prescriptions.AddRange(_pending.OfType<Prescription>());
            Administrations.AddRange(_pending.OfType<MedicationAdministration>());
            Transfers.AddRange(_pending.OfType<ClinicalTransfer>());
            Audit.AddRange(_pending.OfType<ClinicalAuditEntry>());
            _pending.Clear();
            SaveCount++;
            return Task.CompletedTask;
        }

        private TransferRow ToRow(ClinicalTransfer t)
        {
            var encounter = Encounters.Single(e => e.Id == t.ClinicalEncounterId);
            return new TransferRow(
                t.Id, t.ClinicalEncounterId, t.Status,
                t.OriginClinicId is { } origin ? new ClinicSummary(origin, $"Clinic {origin:N}") : null,
                new ClinicSummary(t.DestinationClinicId, $"Clinic {t.DestinationClinicId:N}"),
                null, t.RequestedAtUtc, t.SentAtUtc, t.SentByStaffId, t.ReceivedAtUtc, t.ReceivedByStaffId, t.CancelledAtUtc,
                Patients.SingleOrDefault(p => p.Id == encounter.PatientId)?.ToTestRow(), encounter.TemporaryPatientCode?.Value,
                t.Reason, t.Notes, null, encounter.Status);
        }

        private static MedicationSummary ToSummary(Medication m) =>
            new(m.Id, m.Code, m.Name, m.Presentation, m.Concentration, m.DefaultRoute, m.Status);

        private string MedicationName(Guid medicationId) => Medications.Single(m => m.Id == medicationId).Name;

        private PrescriptionSummary ToSummary(Prescription p) => new(
            p.Id, p.ClinicalEncounterId, p.Status, p.Notes, p.CreatedAtUtc, p.IssuedAtUtc, p.ValidUntilUtc, p.CancelledAtUtc, p.PrescribedByStaffId,
            p.Details.Select(d => new PrescriptionDetailSummary(
                d.Id, d.MedicationId, MedicationName(d.MedicationId), d.Dose, d.DoseUnit, d.Route, d.Frequency, d.DurationDays, d.Instructions)).ToList());

        private EncounterRow ToRow(ClinicalEncounter e) => new(
            e.Id,
            e.PatientId,
            Patients.SingleOrDefault(p => p.Id == e.PatientId)?.ToTestRow(),
            e.TemporaryPatientCode?.Value,
            e.ClinicId,
            e.Type,
            e.Status,
            e.Reason,
            e.StartedAtUtc,
            e.EndedAtUtc,
            e.IdentifiedAtUtc,
            Triages.Where(t => t.ClinicalEncounterId == e.Id).OrderByDescending(t => t.PerformedAtUtc).Select(t => (TriagePriority?)t.Priority).FirstOrDefault());
    }

    internal static class ClinicalTestExtensions
    {
        public static PatientRow ToTestRow(this Patient patient)
        {
            var identifier = patient.Identifiers.Where(i => i.IsActive).OrderByDescending(i => i.IsPrimary).FirstOrDefault();
            return new PatientRow(patient.Id, patient.GivenNames, patient.FamilyNames, patient.DateOfBirth, patient.Sex, patient.IdentityStatus,
                identifier is null ? null : new PatientIdentifierRow(identifier.Type, identifier.NormalizedValue));
        }
    }

    // Composition of the clinical use cases over the fakes, for one user.
    internal sealed class ClinicalHarness
    {
        public static readonly DateTimeOffset Start = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

        // A shared store lets two users act on the same data (e.g. a doctor prescribes, a nurse administers).
        public ClinicalHarness(FakeCurrentUser user, FakeClinicalStore? store = null)
        {
            User = user;
            Store = store ?? new FakeClinicalStore();
            Actor = new CurrentClinicalActor(user, Staff);
            var access = new PatientAccessAuthorizer(user, new NoPatientLinks());
            Patients = new PatientService(Store, Store, user, Staff, access, Time);
            Encounters = new EncounterService(Actor, Store, Store, Staff, access, Time);
            Admissions = new AdmissionService(Actor, Store);
            Records = new EncounterRecordService(Actor, user, Store, Store, access, Time);
            Medications = new MedicationService(Actor, Store, Store, access, Time);
            Transfers = new TransferService(Actor, Store, Store, access, Time);
        }

        public FakeCurrentUser User { get; }
        public FakeStaffDirectory Staff { get; } = new();
        public FakeClinicalStore Store { get; }
        public FixedTime Time { get; } = new(Start);
        public CurrentClinicalActor Actor { get; }
        public PatientService Patients { get; }
        public EncounterService Encounters { get; }
        public AdmissionService Admissions { get; }
        public EncounterRecordService Records { get; }
        public MedicationService Medications { get; }
        public TransferService Transfers { get; }

        public static ClinicalHarness ForRole(string role, int clinics = 1)
        {
            var harness = new ClinicalHarness(FakeCurrentUser.InRole(role));
            harness.Staff.Add(harness.User.UserId!.Value, true, Enumerable.Range(0, clinics).Select(_ => Guid.NewGuid()).ToArray());
            return harness;
        }

        public Guid ClinicId => Staff.FindByIdentityUserAsync(User.UserId!.Value, CancellationToken.None).Result!.ActiveClinicIds.First();

        public Patient SeedPatient()
        {
            var patient = Patient.Register("Ana", "Pérez", new DateOnly(1980, 1, 1), BiologicalSex.Female);
            patient.AddIdentifier(PatientIdentifierType.NationalId, "001-1234567-8", PatientIdentifierSource.Manual, isPrimary: true);
            Store.Patients.Add(patient);
            return patient;
        }

        public ClinicalEncounter SeedEncounter(Guid? clinicId = null, Guid? patientId = null)
        {
            var encounter = patientId is { } id
                ? ClinicalEncounter.CreateForPatient(id, ClinicalEncounterType.Emergency, Start, clinicId ?? ClinicId)
                : ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Emergency, Start, clinicId ?? ClinicId);
            Store.Encounters.Add(encounter);
            return encounter;
        }

        public Medication SeedMedication(string name = "Paracetamol", bool active = true)
        {
            var medication = Medication.Register(name, "Tableta", "500 mg", $"TEST-{Guid.NewGuid():N}"[..20], MedicationRoute.Oral);
            if (!active)
            {
                medication.Deactivate();
            }

            Store.Medications.Add(medication);
            return medication;
        }

        private sealed class NoPatientLinks : IClinicalIdentityLinkService
        {
            public Task<ClinicalSubject?> FindSubjectAsync(InvitationSubjectType type, Guid subjectId, CancellationToken cancellationToken) =>
                Task.FromResult<ClinicalSubject?>(null);

            public Task<bool> LinkAsync(InvitationSubjectType type, Guid subjectId, Guid userId, CancellationToken cancellationToken) =>
                Task.FromResult(false);

            public Task<Guid?> FindPatientIdByUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
        }
    }
}
