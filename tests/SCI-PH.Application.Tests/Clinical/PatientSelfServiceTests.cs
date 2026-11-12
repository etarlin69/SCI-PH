using Microsoft.Extensions.Logging.Abstractions;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Authorization;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Tests.Clinical
{
    // The rules of PatientSelfService that do not depend on the database: own-record resolution, one metadata-only audit
    // entry per read, and fail closed when that entry cannot be saved. What each query returns is tested over the real
    // store in PatientSelfServicePublicationTests (integration).
    public sealed class PatientSelfServiceTests
    {
        private const string Sensitive = "Sensible-Contenido-Clinico";

        private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

        private readonly Guid _userId = Guid.NewGuid();
        private readonly Guid _patientId = Guid.NewGuid();
        private readonly Guid _encounterId = Guid.NewGuid();
        private readonly FakeClinicalStore _store = new();
        private readonly FakeSelfReads _reads = new();

        private PatientSelfService Service(Guid? linkedPatient = null, bool linked = true, string role = SecurityRoles.Patient)
        {
            var user = new FakeCurrentUser(_userId, [role], RolePermissions.For(role));
            var links = new FakeLinks(linked ? linkedPatient ?? _patientId : null);
            return new PatientSelfService(
                user, links, new PatientAccessAuthorizer(user, links), _reads, _store, new FixedTime(Now), NullLogger<PatientSelfService>.Instance);
        }

        private List<(string Resource, Func<PatientSelfService, Task<object?>> Read)> Operations() =>
        [
            (PatientSelfAuditResources.Profile, async s => await s.GetProfileAsync(default)),
            (PatientSelfAuditResources.Summary, async s => await s.GetSummaryAsync(default)),
            (PatientSelfAuditResources.Encounters, async s => await s.ListEncountersAsync(default)),
            (PatientSelfAuditResources.Encounter, async s => await s.GetEncounterAsync(_encounterId, default)),
            (PatientSelfAuditResources.Allergies, async s => await s.ListAllergiesAsync(default)),
            (PatientSelfAuditResources.Diagnoses, async s => await s.ListDiagnosesAsync(default)),
            (PatientSelfAuditResources.Prescriptions, async s => await s.ListPrescriptionsAsync(default)),
            (PatientSelfAuditResources.Results, async s => await s.ListResultsAsync(default))
        ];

        [Fact]
        public async Task TheRecordAlwaysComesFromTheAccountLink()
        {
            _reads.Encounter = Encounter(_encounterId);

            await Service().GetEncounterAsync(_encounterId, default);
            await Service().ListResultsAsync(default);

            Assert.All(_reads.PatientIdsRead, id => Assert.Equal(_patientId, id));
            Assert.NotEmpty(_reads.PatientIdsRead);
        }

        [Fact]
        public async Task EachReadWritesOneEntryWithMetadataOnly()
        {
            _reads.Encounter = Encounter(_encounterId);

            foreach (var (resource, read) in Operations())
            {
                _store.Audit.Clear();
                Assert.NotNull(await read(Service()));

                var entry = Assert.Single(_store.Audit);
                Assert.Equal(resource, entry.EntityName);
                Assert.Equal(AuditActionType.Read, entry.Action);
                Assert.Equal(AuditOutcome.Success, entry.Outcome);
                Assert.Equal(AuditActorType.User, entry.ActorType);
                Assert.Equal(_userId, entry.ActorIdentityUserId);
                Assert.Null(entry.ActorStaffId);
                Assert.Equal(_patientId, entry.PatientId);
                Assert.Equal(Now, entry.OccurredAtUtc);
                AssertNoContent(entry);
            }
        }

        [Fact]
        public async Task AnEncounterReadRecordsThatEncounter()
        {
            _reads.Encounter = Encounter(_encounterId);

            await Service().GetEncounterAsync(_encounterId, default);

            var entry = Assert.Single(_store.Audit);
            Assert.Equal((_encounterId.ToString(), _encounterId), (entry.EntityId, entry.ClinicalEncounterId));
        }

        [Fact]
        public async Task WithoutItsAuditEntryNoReadReturnsThePayload()
        {
            _reads.Encounter = Encounter(_encounterId);
            _store.BeforeSave = () => throw new InvalidOperationException($"database down {Sensitive}");

            foreach (var (_, read) in Operations())
            {
                var error = await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => read(Service()));

                Assert.DoesNotContain(Sensitive, error.Message);
                Assert.DoesNotContain("audit", error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("database", error.Message, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Empty(_store.Audit);
        }

        [Fact]
        public async Task ACancelledRequestStaysCancelledAndIsNotReportedAsAnAuditFailure()
        {
            _store.BeforeSave = () => throw new OperationCanceledException();

            await Assert.ThrowsAsync<OperationCanceledException>(() => Service().ListResultsAsync(default));
        }

        [Fact]
        public async Task AnUnlinkedAccountIsDeniedAuditedAndReadsNothing()
        {
            await Assert.ThrowsAsync<AccessDeniedException>(() => Service(linked: false).GetProfileAsync(default));

            Assert.Empty(_reads.PatientIdsRead);
            var entry = Assert.Single(_store.Audit);
            Assert.Equal(
                (AuditOutcome.Failure, "PatientNotLinked", PatientSelfAuditResources.Profile, (Guid?)null, _userId.ToString()),
                (entry.Outcome, entry.FailureReason, entry.EntityName, entry.PatientId, entry.EntityId));
            AssertNoContent(entry);
        }

        [Fact]
        public async Task AStaffAccountIsDeniedEvenWithClinicalPermissions()
        {
            await Assert.ThrowsAsync<AccessDeniedException>(() => Service(role: SecurityRoles.Doctor).ListDiagnosesAsync(default));

            Assert.Empty(_reads.PatientIdsRead);
            Assert.Equal("NotAPatientAccount", Assert.Single(_store.Audit).FailureReason);
        }

        [Fact]
        public async Task AnEncounterThatIsNotOwnIsNotFoundAndAudited()
        {
            var foreign = Guid.NewGuid();

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service().GetEncounterAsync(foreign, default));

            var entry = Assert.Single(_store.Audit);
            Assert.Equal(
                (AuditOutcome.Failure, "NotFound", foreign.ToString(), (Guid?)_patientId, (Guid?)null),
                (entry.Outcome, entry.FailureReason, entry.EntityId, entry.PatientId, entry.ClinicalEncounterId));
        }

        [Fact]
        public async Task FailingToAuditADenialNeverChangesTheDenial()
        {
            _store.BeforeSave = () => throw new InvalidOperationException("database down");

            await Assert.ThrowsAsync<AccessDeniedException>(() => Service(linked: false).GetProfileAsync(default));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service().GetEncounterAsync(Guid.NewGuid(), default));
        }

        [Fact]
        public void ADiagnosisIsPublishedOnlyWhenDefinitiveAndFromAClosedEncounter()
        {
            Assert.Equal([DiagnosisType.Definitive], PatientSelfVisibility.PublishedDiagnosisTypes);
            Assert.Equal([ClinicalEncounterStatus.Closed], PatientSelfVisibility.PublishedDiagnosisEncounterStatuses);
            Assert.DoesNotContain(DiagnosisType.Presumptive, PatientSelfVisibility.PublishedDiagnosisTypes);
            foreach (var status in Enum.GetValues<ClinicalEncounterStatus>().Where(s => s != ClinicalEncounterStatus.Closed))
            {
                Assert.DoesNotContain(status, PatientSelfVisibility.PublishedDiagnosisEncounterStatuses);
            }
        }

        [Theory]
        [InlineData(ClinicalEncounterStatus.Created, PatientEncounterStatus.InProgress)]
        [InlineData(ClinicalEncounterStatus.InProgress, PatientEncounterStatus.InProgress)]
        [InlineData(ClinicalEncounterStatus.Transferred, PatientEncounterStatus.InProgress)]
        [InlineData(ClinicalEncounterStatus.Received, PatientEncounterStatus.InProgress)]
        [InlineData(ClinicalEncounterStatus.InEvolution, PatientEncounterStatus.InProgress)]
        [InlineData(ClinicalEncounterStatus.Closed, PatientEncounterStatus.Completed)]
        [InlineData(ClinicalEncounterStatus.Cancelled, PatientEncounterStatus.Cancelled)]
        public void InternalEncounterStatesMapToThePatientClassification(ClinicalEncounterStatus internalStatus, PatientEncounterStatus expected) =>
            Assert.Equal(expected, PatientSelfVisibility.EncounterStatus(internalStatus));

        [Fact]
        public void EveryInternalEncounterStateHasACaseInTheMappingTest() =>
            Assert.Equal(7, Enum.GetValues<ClinicalEncounterStatus>().Length);

        private static PatientSelfEncounter Encounter(Guid id) =>
            new(id, ClinicalEncounterType.Consultation, PatientEncounterStatus.Completed, Now, Now, "Hospital");

        private static void AssertNoContent(ClinicalAuditEntry entry)
        {
            Assert.Null(entry.ChangesJson);
            var recorded = $"{entry.EntityName}|{entry.EntityId}|{entry.FailureReason}|{entry.ActorReference}|{entry.CorrelationId}";
            Assert.DoesNotContain(Sensitive, recorded);
        }

        private sealed class FakeLinks(Guid? patientId) : IClinicalIdentityLinkService
        {
            public Task<ClinicalSubject?> FindSubjectAsync(InvitationSubjectType type, Guid subjectId, CancellationToken cancellationToken) =>
                Task.FromResult<ClinicalSubject?>(null);

            public Task<bool> LinkAsync(InvitationSubjectType type, Guid subjectId, Guid userId, CancellationToken cancellationToken) =>
                Task.FromResult(false);

            public Task<Guid?> FindPatientIdByUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(patientId);
        }

        // Every value holds the marker: it must never end up in the audit entry or an error.
        private sealed class FakeSelfReads : IPatientSelfReadStore
        {
            public PatientSelfEncounter? Encounter { get; set; }

            public List<Guid> PatientIdsRead { get; } = [];

            private T Read<T>(Guid patientId, T value)
            {
                PatientIdsRead.Add(patientId);
                return value;
            }

            public Task<PatientSelfProfileRow?> GetProfileAsync(Guid patientId, CancellationToken cancellationToken) =>
                Task.FromResult<PatientSelfProfileRow?>(Read(patientId, new PatientSelfProfileRow(
                    Sensitive, Sensitive, null, BiologicalSex.Female, new PatientIdentifierRow(PatientIdentifierType.NationalId, "40212345678"), Sensitive, Sensitive)));

            public Task<int> CountEncountersAsync(Guid patientId, CancellationToken cancellationToken) => Task.FromResult(Read(patientId, 1));

            public Task<IReadOnlyList<PatientSelfEncounter>> ListEncountersAsync(Guid patientId, int limit, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<PatientSelfEncounter>>(Read(patientId, new List<PatientSelfEncounter>()));

            public Task<PatientSelfEncounter?> GetEncounterAsync(Guid patientId, Guid encounterId, CancellationToken cancellationToken) =>
                Task.FromResult(Read(patientId, Encounter?.Id == encounterId ? Encounter : null));

            public Task<IReadOnlyList<PatientSelfAllergy>> ListAllergiesAsync(Guid patientId, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<PatientSelfAllergy>>(Read(patientId, new List<PatientSelfAllergy>()));

            public Task<IReadOnlyList<PatientSelfDiagnosis>> ListDiagnosesAsync(Guid patientId, Guid? encounterId, int limit, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<PatientSelfDiagnosis>>(Read(patientId, new List<PatientSelfDiagnosis>()));

            public Task<IReadOnlyList<PatientSelfPrescription>> ListPrescriptionsAsync(Guid patientId, Guid? encounterId, int limit, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<PatientSelfPrescription>>(Read(patientId, new List<PatientSelfPrescription>()));

            public Task<int> CountResultsAsync(Guid patientId, CancellationToken cancellationToken) => Task.FromResult(Read(patientId, 0));

            public Task<IReadOnlyList<PatientSelfResult>> ListResultsAsync(Guid patientId, Guid? encounterId, int limit, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<PatientSelfResult>>(Read(patientId, new List<PatientSelfResult>()));
        }
    }
}
