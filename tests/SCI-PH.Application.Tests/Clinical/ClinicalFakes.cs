using Microsoft.Extensions.Caching.Memory;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Authorization;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Application.Features.Devices;
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

    // MemoryCache reads expirations on this clock, so a test can move time past an entry's lifetime.
    internal sealed class FixedTimeClock(FixedTime time) : Microsoft.Extensions.Internal.ISystemClock
    {
        public DateTimeOffset UtcNow => time.Now;
    }

    // In-memory clinical store: writes become visible only after SaveChangesAsync, like a transaction. It also holds the
    // idempotency receipts, committed by the same SaveChangesAsync (as ClinicalWriteStore + ClinicalIdempotencyStore).
    // It also holds the fingerprint sessions (as FingerprintSessionStore): an update commits only while the stored status
    // is still the expected one, and the unique active-session rules are checked on save, as the indexes would.
    internal sealed class FakeClinicalStore : IClinicalReadStore, IClinicalWriteStore, IIdempotencyStore, IFingerprintSessionStore, IFingerprintEnrollmentStore,
        IFingerprintSlotDeletionStore, IFaceTemplateDeletionStore
    {
        private readonly List<object> _pending = [];

        public List<Patient> Patients { get; } = [];
        public List<ClinicalEncounter> Encounters { get; } = [];
        public List<EncounterStaffAssignment> Assignments { get; } = [];
        public List<IdempotencyReceipt> Receipts { get; } = [];
        // Runs at the start of SaveChangesAsync: lets a test commit a competing request first (a race).
        public Action? BeforeSave { get; set; }
        // Runs after a receipt lookup has answered: lets a test commit a competing request right after this one found no
        // receipt and before it loads anything else (the window between the lookup and the encounter load).
        public Action? AfterReceiptLookup { get; set; }
        public List<Triage> Triages { get; } = [];
        public List<VitalSign> VitalSigns { get; } = [];
        public List<ClinicalNote> Notes { get; } = [];
        public List<Diagnosis> Diagnoses { get; } = [];
        public List<ClinicalProcedure> Procedures { get; } = [];
        public List<Medication> Medications { get; } = [];
        public List<Prescription> Prescriptions { get; } = [];
        public List<MedicationAdministration> Administrations { get; } = [];
        public List<ClinicalTransfer> Transfers { get; } = [];
        // Clinics that exist, with their status. Unknown ids do not exist.
        public Dictionary<Guid, ClinicStatus> Clinics { get; } = [];
        public HashSet<Guid> GuardedEncounters { get; } = [];
        public List<ClinicalAuditEntry> Audit { get; } = [];
        public List<PatientBiometricReference> BiometricReferences { get; } = [];
        public List<IdentificationAttempt> Attempts { get; } = [];
        public List<FingerprintSession> Sessions { get; } = [];
        public int SaveCount { get; private set; }

        public Task<BiometricReferenceRow?> FindActiveFingerprintReferenceAsync(
            string provider, string subjectReference, CancellationToken cancellationToken) =>
            Task.FromResult(BiometricReferences
                .Where(r => r.Modality == BiometricModality.Fingerprint && r.Provider == provider
                    && r.SubjectReference == subjectReference && r.Status == BiometricReferenceStatus.Active)
                .Select(r => new BiometricReferenceRow(r.Id, r.PatientId))
                .SingleOrDefault());

        public Task<Guid?> GetActiveBiometricReferencePatientIdAsync(Guid biometricReferenceId, CancellationToken cancellationToken) =>
            Task.FromResult(BiometricReferences
                .Where(r => r.Id == biometricReferenceId && r.Status == BiometricReferenceStatus.Active)
                .Select(r => (Guid?)r.PatientId)
                .SingleOrDefault());

        public void Add(IdentificationAttempt attempt) => _pending.Add(attempt);

        public void Add(PatientBiometricReference reference) => _pending.Add(reference);

        public Task<PatientBiometricReference?> FindBiometricReferenceAsync(Guid referenceId, CancellationToken cancellationToken) =>
            Task.FromResult(BiometricReferences.SingleOrDefault(r => r.Id == referenceId));

        // The status the reference had when it was used: the save fails if it changed meanwhile (the concurrency token).
        public void EnsureBiometricReferenceUnchanged(PatientBiometricReference reference) =>
            _pending.Add(new ReferenceGuard(reference, reference.Status));

        private sealed record ReferenceGuard(PatientBiometricReference Reference, BiometricReferenceStatus Status);

        public Task<bool> HasActiveFingerprintReferenceAsync(
            Guid patientId, string provider, string subjectReferencePrefix, CancellationToken cancellationToken) =>
            Task.FromResult(BiometricReferences.Any(r => r.PatientId == patientId && r.Modality == BiometricModality.Fingerprint
                && r.Provider == provider && r.Status == BiometricReferenceStatus.Active
                && r.SubjectReference.StartsWith(subjectReferencePrefix, StringComparison.Ordinal)));

        public Task<IReadOnlyList<string>> ListActiveFingerprintSubjectReferencesAsync(
            string provider, string subjectReferencePrefix, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(BiometricReferences
                .Where(r => r.Modality == BiometricModality.Fingerprint && r.Provider == provider && r.Status == BiometricReferenceStatus.Active
                    && r.SubjectReference.StartsWith(subjectReferencePrefix, StringComparison.Ordinal))
                .Select(r => r.SubjectReference)
                .ToList());

        public Task<IReadOnlyList<FingerprintReferenceRow>> ListFingerprintReferencesAsync(Guid patientId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FingerprintReferenceRow>>(BiometricReferences
                .Where(r => r.PatientId == patientId && r.Modality == BiometricModality.Fingerprint)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Select(r => new FingerprintReferenceRow(r.Id, r.Provider, r.SubjectReference, r.Status, r.CreatedAtUtc, r.RevokedAtUtc))
                .ToList());

        public Task<IReadOnlyList<FaceReferenceRow>> ListFaceReferencesAsync(Guid patientId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FaceReferenceRow>>(BiometricReferences
                .Where(r => r.PatientId == patientId && r.Modality == BiometricModality.Face)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Select(ToFaceRow)
                .ToList());

        public Task<FaceReferenceRow?> FindActiveFaceReferenceAsync(Guid patientId, CancellationToken cancellationToken) =>
            Task.FromResult(BiometricReferences
                .Where(r => r.PatientId == patientId && r.Modality == BiometricModality.Face && r.Status == BiometricReferenceStatus.Active)
                .Select(ToFaceRow)
                .SingleOrDefault());

        public Task<string?> GetAuditChangesAsync(Guid auditEntryId, string entityName, Guid patientId, CancellationToken cancellationToken) =>
            Task.FromResult(Audit.SingleOrDefault(a => a.Id == auditEntryId && a.EntityName == entityName && a.PatientId == patientId)?.ChangesJson);

        private static FaceReferenceRow ToFaceRow(PatientBiometricReference r) =>
            new(r.Id, r.Provider, r.SubjectReference, r.AlgorithmVersion, r.Status, r.CreatedAtUtc, r.RevokedAtUtc);

        // ---- Enrollments (as FingerprintEnrollmentStore) ----
        public List<FingerprintEnrollment> Enrollments { get; } = [];

        // Same signatures as the identification sessions' store: implemented explicitly.
        Task<FingerprintEnrollment?> IFingerprintEnrollmentStore.FindAsync(Guid enrollmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Enrollments.SingleOrDefault(x => x.Id == enrollmentId));

        Task<FingerprintEnrollment?> IFingerprintEnrollmentStore.FindPendingForReaderAsync(Guid deviceCredentialId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(Enrollments.SingleOrDefault(x => x.DeviceCredentialId == deviceCredentialId && x.IsPending && x.ExpiresAtUtc > now));

        public Task ExpireDueAsync(FingerprintEnrollmentScope scope, DateTimeOffset now, CancellationToken cancellationToken)
        {
            for (var i = 0; i < Enrollments.Count; i++)
            {
                var x = Enrollments[i];
                if (x.IsDue(now) && (x.Id == scope.EnrollmentId || x.PatientId == scope.PatientId || x.DeviceCredentialId == scope.DeviceCredentialId))
                {
                    Enrollments[i] = x with { Status = FingerprintEnrollmentStatus.Expired };
                }
            }

            return Task.CompletedTask;
        }

        public void Add(FingerprintEnrollment enrollment) => _pending.Add(enrollment);

        public void Update(FingerprintEnrollment enrollment, FingerprintEnrollmentStatus expectedStatus) =>
            _pending.Add(new EnrollmentUpdate(enrollment, expectedStatus));

        private sealed record EnrollmentUpdate(FingerprintEnrollment Enrollment, FingerprintEnrollmentStatus ExpectedStatus);

        // ---- Slot deletion jobs (as FingerprintSlotDeletionStore) ----
        public List<FingerprintSlotDeletion> Deletions { get; } = [];

        private FingerprintSlotDeletionTarget Target(FingerprintSlotDeletion deletion)
        {
            var reference = BiometricReferences.Single(r => r.Id == deletion.BiometricReferenceId);
            return new FingerprintSlotDeletionTarget(deletion, reference.PatientId, reference.SubjectReference);
        }

        Task<FingerprintSlotDeletionTarget?> IFingerprintSlotDeletionStore.FindAsync(Guid deletionId, CancellationToken cancellationToken) =>
            Task.FromResult(Deletions.SingleOrDefault(x => x.Id == deletionId) is { } deletion ? Target(deletion) : null);

        public Task<FingerprintSlotDeletionTarget?> FindOldestPendingForReaderAsync(Guid deviceCredentialId, CancellationToken cancellationToken) =>
            Task.FromResult(Deletions.Where(x => x.DeviceCredentialId == deviceCredentialId && x.IsPending).OrderBy(x => x.CreatedAtUtc)
                .FirstOrDefault() is { } deletion ? Target(deletion) : null);

        public Task<IReadOnlyDictionary<Guid, FingerprintSlotDeletion>> ListByReferencesAsync(
            IReadOnlyCollection<Guid> biometricReferenceIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, FingerprintSlotDeletion>>(Deletions
                .Where(x => biometricReferenceIds.Contains(x.BiometricReferenceId)).ToDictionary(x => x.BiometricReferenceId));

        public Task<bool> HasPendingForSubjectAsync(Guid deviceCredentialId, string subjectReference, CancellationToken cancellationToken) =>
            Task.FromResult(Deletions.Any(x => x.DeviceCredentialId == deviceCredentialId && x.IsPending
                && Target(x).SubjectReference == subjectReference));

        public Task<IReadOnlyList<string>> ListPendingSubjectReferencesForReaderAsync(Guid deviceCredentialId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Deletions.Where(x => x.DeviceCredentialId == deviceCredentialId && x.IsPending)
                .Select(x => Target(x).SubjectReference).ToList());

        public void Add(FingerprintSlotDeletion deletion) => _pending.Add(deletion);

        public void Update(FingerprintSlotDeletion deletion, FingerprintSlotDeletionStatus expectedStatus, Guid? expectedLastAttemptId) =>
            _pending.Add(new DeletionUpdate(deletion, expectedStatus, expectedLastAttemptId));

        private sealed record DeletionUpdate(FingerprintSlotDeletion Deletion, FingerprintSlotDeletionStatus ExpectedStatus, Guid? ExpectedLastAttemptId);

        // ---- Face template deletion jobs (as FaceTemplateDeletionStore) ----
        public List<FaceTemplateDeletion> FaceDeletions { get; } = [];

        private FaceTemplateDeletionTarget FaceTarget(FaceTemplateDeletion deletion)
        {
            var reference = BiometricReferences.Single(r => r.Id == deletion.BiometricReferenceId);
            return new FaceTemplateDeletionTarget(deletion, reference.PatientId, reference.Modality, reference.Provider, reference.SubjectReference);
        }

        Task<FaceTemplateDeletionTarget?> IFaceTemplateDeletionStore.FindAsync(Guid deletionId, CancellationToken cancellationToken) =>
            Task.FromResult(FaceDeletions.SingleOrDefault(x => x.Id == deletionId) is { } deletion ? FaceTarget(deletion) : null);

        public Task<IReadOnlyList<FaceTemplateDeletionTarget>> ListPendingAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FaceTemplateDeletionTarget>>(FaceDeletions.Where(x => x.IsPending)
                .OrderBy(x => x.CreatedAtUtc).Take(limit).Select(FaceTarget).ToList());

        public Task<IReadOnlyList<RetainedFaceTemplate>> ListRetainedAsync(string provider, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RetainedFaceTemplate>>(BiometricReferences
                .Where(r => r.Modality == BiometricModality.Face && r.Provider == provider)
                .Where(r => r.Status == BiometricReferenceStatus.Active
                    || FaceDeletions.SingleOrDefault(x => x.BiometricReferenceId == r.Id) is not { IsPending: false })
                .Select(r => new RetainedFaceTemplate(r.SubjectReference, r.Status == BiometricReferenceStatus.Active))
                .ToList());

        public void Add(FaceTemplateDeletion deletion) => _pending.Add(deletion);

        public void Update(FaceTemplateDeletion deletion, FaceTemplateDeletionStatus expectedStatus, int expectedAttempts) =>
            _pending.Add(new FaceDeletionUpdate(deletion, expectedStatus, expectedAttempts));

        private sealed record FaceDeletionUpdate(FaceTemplateDeletion Deletion, FaceTemplateDeletionStatus ExpectedStatus, int ExpectedAttempts);

        public Task<FingerprintSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.SingleOrDefault(x => x.Id == sessionId));

        public Task<FingerprintSession?> FindPendingForReaderAsync(Guid deviceCredentialId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.SingleOrDefault(x =>
                x.DeviceCredentialId == deviceCredentialId && x.Status == FingerprintSessionStatus.Pending && x.ExpiresAtUtc > now));

        public Task ExpireDueAsync(FingerprintSessionScope scope, DateTimeOffset now, CancellationToken cancellationToken)
        {
            for (var i = 0; i < Sessions.Count; i++)
            {
                var x = Sessions[i];
                if (x.IsDue(now) && (x.Id == scope.SessionId || x.ClinicalEncounterId == scope.ClinicalEncounterId
                    || x.DeviceCredentialId == scope.DeviceCredentialId))
                {
                    Sessions[i] = x with { Status = FingerprintSessionStatus.Expired };
                }
            }

            return Task.CompletedTask;
        }

        public void Add(FingerprintSession session) => _pending.Add(session);

        public void Update(FingerprintSession session, FingerprintSessionStatus expectedStatus) =>
            _pending.Add(new SessionUpdate(session, expectedStatus));

        private sealed record SessionUpdate(FingerprintSession Session, FingerprintSessionStatus ExpectedStatus);

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

        public Task<PatientIdentifierMatch?> FindPlatformIdentifierAsync(
            PatientIdentifierType type, string normalizedValue, CancellationToken cancellationToken) =>
            Task.FromResult(Patients.SelectMany(p => p.Identifiers)
                .Where(i => i.IsActive && i.Type == type && i.Issuer == string.Empty && i.NormalizedValue == normalizedValue)
                .Select(i => new PatientIdentifierMatch(i.Id, i.PatientId))
                .SingleOrDefault());

        public Task<IReadOnlyList<PatientRow>> FindPatientsByNameAsync(string term, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PatientRow>>(Patients
                .Where(p => $"{p.GivenNames} {p.FamilyNames}".Contains(term, StringComparison.OrdinalIgnoreCase))
                .Take(limit).Select(p => p.ToTestRow()).ToList());

        public Task<EncounterRow?> GetEncounterAsync(Guid encounterId, CancellationToken cancellationToken) =>
            Task.FromResult(Encounters.SingleOrDefault(e => e.Id == encounterId) is { } e ? ToRow(e) : null);

        public Task<bool> HasActiveStaffAssignmentAsync(Guid encounterId, Guid staffId, CancellationToken cancellationToken) =>
            Task.FromResult(Assignments.Any(a => a.ClinicalEncounterId == encounterId && a.ClinicalStaffId == staffId && a.IsActive));

        public Task<IdempotencyReceipt?> FindAsync(IdempotencyKey key, CancellationToken cancellationToken)
        {
            var found = Receipts.SingleOrDefault(r => r.Key == key);
            AfterReceiptLookup?.Invoke();
            return Task.FromResult(found);
        }

        public void Add(IdempotencyReceipt receipt) => _pending.Add(receipt);

        public Task<IReadOnlyList<AssignedEmergencyRow>> ListAssignedPrehospitalEmergenciesAsync(
            Guid staffId, IReadOnlyCollection<ClinicalEncounterStatus> statuses, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssignedEmergencyRow>>(Encounters
                .Where(e => e.Type == ClinicalEncounterType.Prehospital && e.ClinicId is null && statuses.Contains(e.Status)
                    && Assignments.Any(a => a.ClinicalEncounterId == e.Id && a.ClinicalStaffId == staffId && a.IsActive))
                .OrderByDescending(e => e.StartedAtUtc)
                .Take(limit)
                .Select(e => new AssignedEmergencyRow(
                    ToRow(e),
                    Transfers.Where(t => t.ClinicalEncounterId == e.Id && ClinicalTransfer.ActiveStatuses.Contains(t.Status))
                        .Select(t => (ClinicalTransferStatus?)t.Status).SingleOrDefault()))
                .ToList());

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

        public Task<IReadOnlyList<ClinicalProcedureSummary>> ListProceduresAsync(Guid encounterId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClinicalProcedureSummary>>(Procedures.Where(p => p.ClinicalEncounterId == encounterId)
                .OrderByDescending(p => p.PerformedAtUtc.HasValue).ThenByDescending(p => p.PerformedAtUtc).ThenBy(p => p.Id)
                .Select(p => new ClinicalProcedureSummary(
                    p.Id, p.ClinicalEncounterId, p.Name, p.CodeSystem, p.Code, p.Status, p.PerformedAtUtc, p.CompletedAtUtc, p.Outcome))
                .ToList());

        public Task<IReadOnlyList<TransferRow>> ListEncounterTransfersAsync(Guid encounterId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TransferRow>>(Transfers.Where(t => t.ClinicalEncounterId == encounterId)
                .OrderByDescending(t => t.RequestedAtUtc).Take(limit).Select(ToRow).ToList());

        public Task<ClinicStatus?> GetClinicStatusAsync(Guid clinicId, CancellationToken cancellationToken) =>
            Task.FromResult(Clinics.TryGetValue(clinicId, out var status) ? status : (ClinicStatus?)null);

        public Task<bool> HasActiveTransferAsync(Guid encounterId, CancellationToken cancellationToken) =>
            Task.FromResult(Transfers.Any(t => t.ClinicalEncounterId == encounterId
                && ClinicalTransfer.ActiveStatuses.Contains(t.Status)));

        public Task<Guid?> FindActiveTransferIdAsync(Guid encounterId, CancellationToken cancellationToken) =>
            Task.FromResult(Transfers.Where(t => t.ClinicalEncounterId == encounterId && ClinicalTransfer.ActiveStatuses.Contains(t.Status))
                .Select(t => (Guid?)t.Id).SingleOrDefault());

        // Transfer destinations: the clinics of Clinics with a name, a type and a place (see SeedDestination).
        public Dictionary<Guid, (string Name, ClinicType Type, string Municipality, string Province)> ClinicDetails { get; } = [];

        public Task<IReadOnlyList<TransferDestinationRow>> ListTransferDestinationsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TransferDestinationRow>>(Clinics.Keys.Select(Destination)
                .Where(d => d.Status == ClinicStatus.Active).OrderBy(d => d.Name).ThenBy(d => d.Municipality).ThenBy(d => d.Id).ToList());

        public Task<TransferDestinationRow?> GetTransferDestinationAsync(Guid clinicId, CancellationToken cancellationToken) =>
            Task.FromResult(Clinics.ContainsKey(clinicId) ? Destination(clinicId) : null);

        private TransferDestinationRow Destination(Guid clinicId) =>
            ClinicDetails.TryGetValue(clinicId, out var d)
                ? new TransferDestinationRow(clinicId, d.Name, d.Type, d.Municipality, d.Province, Clinics[clinicId])
                : new TransferDestinationRow(clinicId, $"Clinic {clinicId:N}", ClinicType.Hospital, "Municipio", "Provincia", Clinics[clinicId]);

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
        public void Add(EncounterStaffAssignment assignment) => _pending.Add(assignment);
        public void Add(Triage triage) => _pending.Add(triage);
        public void Add(VitalSign vitalSign) => _pending.Add(vitalSign);
        public void Add(ClinicalNote note) => _pending.Add(note);
        public void Add(Diagnosis diagnosis) => _pending.Add(diagnosis);
        public void Add(ClinicalProcedure procedure) => _pending.Add(procedure);
        public void Add(Prescription prescription) => _pending.Add(prescription);
        public void Add(MedicationAdministration administration) => _pending.Add(administration);
        public void Add(ClinicalAuditEntry auditEntry) => _pending.Add(auditEntry);
        public void EnsureEncounterUnchanged(ClinicalEncounter encounter) => GuardedEncounters.Add(encounter.Id);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            BeforeSave?.Invoke();
            // The unique key of the receipts: the whole save is discarded, as the transaction would be rolled back.
            if (_pending.OfType<IdempotencyReceipt>().Any(pending => Receipts.Any(r => r.Key == pending.Key)))
            {
                _pending.Clear();
                throw new IdempotencyKeyCollisionException(new InvalidOperationException("unique violation (fake)"));
            }

            // Every check before any write, as one transaction.
            if (_pending.OfType<ReferenceGuard>().Any(g => g.Reference.Status != g.Status))
            {
                _pending.Clear();
                throw new ResourceConflictException("The record was changed by another operation. Reload it and try again.");
            }

            CommitSessions();
            CommitDeletions();
            CommitFaceDeletions();
            CommitEnrollmentsAndReferences();
            Receipts.AddRange(_pending.OfType<IdempotencyReceipt>());
            Assignments.AddRange(_pending.OfType<EncounterStaffAssignment>());
            Patients.AddRange(_pending.OfType<Patient>());
            Encounters.AddRange(_pending.OfType<ClinicalEncounter>());
            Triages.AddRange(_pending.OfType<Triage>());
            VitalSigns.AddRange(_pending.OfType<VitalSign>());
            Notes.AddRange(_pending.OfType<ClinicalNote>());
            Diagnoses.AddRange(_pending.OfType<Diagnosis>());
            Procedures.AddRange(_pending.OfType<ClinicalProcedure>());
            Prescriptions.AddRange(_pending.OfType<Prescription>());
            Administrations.AddRange(_pending.OfType<MedicationAdministration>());
            Transfers.AddRange(_pending.OfType<ClinicalTransfer>());
            Audit.AddRange(_pending.OfType<ClinicalAuditEntry>());
            Attempts.AddRange(_pending.OfType<IdentificationAttempt>());
            _pending.Clear();
            SaveCount++;
            return Task.CompletedTask;
        }

        // Status token and unique active-session indexes: any violation discards the whole save.
        private void CommitSessions()
        {
            var updates = _pending.OfType<SessionUpdate>().ToList();
            var added = _pending.OfType<FingerprintSession>().ToList();
            if (updates.Any(u => Sessions.Single(x => x.Id == u.Session.Id).Status != u.ExpectedStatus))
            {
                _pending.Clear();
                throw new ResourceConflictException("The record was changed by another operation. Reload it and try again.");
            }

            var after = Sessions.Where(x => updates.All(u => u.Session.Id != x.Id))
                .Concat(updates.Select(u => u.Session)).Concat(added).ToList();
            if (after.Where(x => x.IsActive).GroupBy(x => x.ClinicalEncounterId).Any(g => g.Count() > 1)
                || after.Where(x => x.Status == FingerprintSessionStatus.Pending).GroupBy(x => x.DeviceCredentialId).Any(g => g.Count() > 1))
            {
                _pending.Clear();
                throw new ResourceConflictException("The emergency or the reader already has an active fingerprint session.");
            }

            Sessions.Clear();
            Sessions.AddRange(after);
        }

        // Status and last-attempt tokens and one job per reference: any violation discards the whole save.
        private void CommitDeletions()
        {
            var updates = _pending.OfType<DeletionUpdate>().ToList();
            var added = _pending.OfType<FingerprintSlotDeletion>().ToList();
            if (updates.Any(u => Deletions.Single(x => x.Id == u.Deletion.Id) is var stored
                    && (stored.Status != u.ExpectedStatus || stored.LastAttemptId != u.ExpectedLastAttemptId))
                || added.Any(a => Deletions.Any(x => x.BiometricReferenceId == a.BiometricReferenceId))
                || added.GroupBy(a => a.BiometricReferenceId).Any(g => g.Count() > 1))
            {
                _pending.Clear();
                throw new ResourceConflictException("The record was changed by another operation. Reload it and try again.");
            }

            for (var i = 0; i < Deletions.Count; i++)
            {
                if (updates.SingleOrDefault(u => u.Deletion.Id == Deletions[i].Id) is { } update)
                {
                    Deletions[i] = update.Deletion;
                }
            }

            Deletions.AddRange(added);
        }

        // Status and attempts tokens and one job per reference: any violation discards the whole save.
        private void CommitFaceDeletions()
        {
            var updates = _pending.OfType<FaceDeletionUpdate>().ToList();
            var added = _pending.OfType<FaceTemplateDeletion>().ToList();
            if (updates.Any(u => FaceDeletions.Single(x => x.Id == u.Deletion.Id) is var stored
                    && (stored.Status != u.ExpectedStatus || stored.Attempts != u.ExpectedAttempts))
                || added.Any(a => FaceDeletions.Any(x => x.BiometricReferenceId == a.BiometricReferenceId))
                || added.GroupBy(a => a.BiometricReferenceId).Any(g => g.Count() > 1))
            {
                _pending.Clear();
                throw new ResourceConflictException("The record was changed by another operation. Reload it and try again.");
            }

            for (var i = 0; i < FaceDeletions.Count; i++)
            {
                if (updates.SingleOrDefault(u => u.Deletion.Id == FaceDeletions[i].Id) is { } update)
                {
                    FaceDeletions[i] = update.Deletion;
                }
            }

            FaceDeletions.AddRange(added);
        }

        // Reference status token, the unique ACTIVE subject reference and the Pending-enrollment indexes: any violation
        // discards the whole save.
        private void CommitEnrollmentsAndReferences()
        {
            var updates = _pending.OfType<EnrollmentUpdate>().ToList();
            var added = _pending.OfType<FingerprintEnrollment>().ToList();
            var references = _pending.OfType<PatientBiometricReference>().ToList();
            if (updates.Any(u => Enrollments.Single(x => x.Id == u.Enrollment.Id).Status != u.ExpectedStatus)
                || references.Any(r => BiometricReferences.Any(existing => existing.Status == BiometricReferenceStatus.Active
                    && existing.Provider == r.Provider && existing.Modality == r.Modality && existing.SubjectReference == r.SubjectReference))
                // UX_PatientBiometricReferences_ActiveFacePerPatient: one ACTIVE face reference per patient.
                || references.Any(r => r.Modality == BiometricModality.Face && BiometricReferences.Any(existing =>
                    existing.PatientId == r.PatientId && existing.Modality == BiometricModality.Face && existing.Status == BiometricReferenceStatus.Active)))
            {
                _pending.Clear();
                throw new ResourceConflictException("The record was changed by another operation. Reload it and try again.");
            }

            var after = Enrollments.Where(x => updates.All(u => u.Enrollment.Id != x.Id))
                .Concat(updates.Select(u => u.Enrollment)).Concat(added).ToList();
            if (after.Where(x => x.IsPending).GroupBy(x => x.PatientId).Any(g => g.Count() > 1)
                || after.Where(x => x.IsPending).GroupBy(x => x.DeviceCredentialId).Any(g => g.Count() > 1))
            {
                _pending.Clear();
                throw new ResourceConflictException("The patient or the reader already has a pending fingerprint enrollment.");
            }

            Enrollments.Clear();
            Enrollments.AddRange(after);
            BiometricReferences.AddRange(references);
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

    // Device credentials as the readers list sees them (no keys). Only the reads the fingerprint identification uses.
    internal sealed class FakeReaders : IDeviceCredentialStore
    {
        public List<DeviceSummary> Devices { get; } = [];

        public DeviceSummary Add(string code, DeviceCredentialStatus status = DeviceCredentialStatus.Active, params string[] scopes)
        {
            var device = new DeviceSummary(Guid.NewGuid(), code, $"Lector {code}", scopes.Length == 0 ? [DeviceScopes.FingerprintIdentify] : scopes,
                status, null, ClinicalHarness.Start, null, null, status == DeviceCredentialStatus.Revoked ? ClinicalHarness.Start : null);
            Devices.Add(device);
            return device;
        }

        public Task<IReadOnlyList<DeviceSummary>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DeviceSummary>>(Devices);

        public Task<DeviceSummary?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Devices.SingleOrDefault(d => d.Id == id));

        public Task<Guid?> AddAsync(NewDeviceCredential credential, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StoredDeviceCredential?> FindByCodeAsync(string deviceCode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ReplaceSecretAsync(Guid id, string secretHash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RevokeAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RecordSeenAsync(Guid id, string? firmwareVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
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

        // A shared store lets two users act on the same data (e.g. a doctor prescribes, a nurse administers); sharedAi lets
        // them use the same clinical AI provider and summary cache, as the singletons of one host.
        public ClinicalHarness(FakeCurrentUser user, FakeClinicalStore? store = null, ClinicalHarness? sharedAi = null)
        {
            User = user;
            Store = store ?? new FakeClinicalStore();
            Actor = new CurrentClinicalActor(user, Staff);
            var access = new PatientAccessAuthorizer(user, new NoPatientLinks());
            Patients = new PatientService(Store, Store, user, Staff, access, Time);
            var encounterAccess = new EncounterAccessService(Store);
            Encounters = new EncounterService(Actor, encounterAccess, Store, Store, Staff, access, Time);
            Admissions = new AdmissionService(Actor, Store);
            Records = new EncounterRecordService(Actor, user, Store, Store, access, Time);
            Medications = new MedicationService(Actor, Store, Store, access, Time);
            Transfers = new TransferService(Actor, Store, Store, access, Time);
            Emergencies = new PrehospitalEmergencyService(Actor, encounterAccess, Store, Store, Store, access, Encounters, Time);
            Documents = new EmergencyDocumentIdentificationService(Actor, Emergencies, Encounters, Store, access, Store, Store, Time, DocumentLogs);
            Fingerprints = new EmergencyFingerprintService(Actor, Emergencies, Encounters, Store, Store, Store, Store, Readers, Time);
            ReaderSide = new FingerprintReaderService(Store, Store, Store, Time);
            FingerprintEnrollments = new FingerprintEnrollmentService(Actor, access, Store, Store, Store, Store, Readers, Store, Time);
            EnrollmentReaderSide = new FingerprintEnrollmentReaderService(Store, Store, Store, Store, Time);
            FingerprintRevocations = new FingerprintRevocationService(Actor, access, Store, Store, Store, Readers, Time);
            DeletionReaderSide = new FingerprintSlotDeletionReaderService(Store, Store, Time);
            FaceCleanup = new FaceTemplateCleanupService(Store, Store, FaceProvider, Time);
            Faces = new PatientFaceService(Actor, access, Store, Store, Store, FaceProvider, new PassThroughCaptureNormalizer(), Store, FaceCleanup, Time);
            AiProvider = sharedAi?.AiProvider ?? new TestClinicalAiProvider();
            AiCache = sharedAi?.AiCache ?? new MemoryCache(new MemoryCacheOptions { SizeLimit = 500, Clock = new FixedTimeClock(Time) });
            AiSummaries = new EncounterAiSummaryService(Actor, Store, Store, access, AiProvider, AiCache, AiMetrics, Time, AiLogs);
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
        public FaceTemplateCleanupService FaceCleanup { get; }
        public PrehospitalEmergencyService Emergencies { get; }
        public FakeReaders Readers { get; } = new();
        public EmergencyFingerprintService Fingerprints { get; }
        public FingerprintReaderService ReaderSide { get; }
        public FingerprintEnrollmentService FingerprintEnrollments { get; }
        public FingerprintEnrollmentReaderService EnrollmentReaderSide { get; }
        public FingerprintRevocationService FingerprintRevocations { get; }
        public FingerprintSlotDeletionReaderService DeletionReaderSide { get; }
        public TestFaceProvider FaceProvider { get; } = new();
        public PatientFaceService Faces { get; }
        public TestClinicalAiProvider AiProvider { get; }
        public CapturingLogger<EncounterAiSummaryService> AiLogs { get; } = new();
        public CapturingLogger<EmergencyDocumentIdentificationService> DocumentLogs { get; } = new();
        public EmergencyDocumentIdentificationService Documents { get; }
        // The summary cache as the composition root registers it (dedicated, bounded), on this harness's clock.
        public MemoryCache AiCache { get; }
        public ClinicalAiMetrics AiMetrics { get; } = new();
        public EncounterAiSummaryService AiSummaries { get; }

        public static ClinicalHarness ForRole(string role, int clinics = 1)
        {
            var harness = new ClinicalHarness(FakeCurrentUser.InRole(role));
            harness.Staff.Add(harness.User.UserId!.Value, true, Enumerable.Range(0, clinics).Select(_ => Guid.NewGuid()).ToArray());
            return harness;
        }

        public Guid ClinicId => Staff.FindByIdentityUserAsync(User.UserId!.Value, CancellationToken.None).Result!.ActiveClinicIds.First();

        public Guid StaffId => Staff.FindByIdentityUserAsync(User.UserId!.Value, CancellationToken.None).Result!.StaffId;

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
