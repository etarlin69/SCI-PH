using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;

namespace SCI_PH.Application.Tests.Clinical
{
    public class NotesDiagnosesCloseTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        private static (ClinicalHarness Harness, ClinicalEncounter Encounter) InProgress(string role = SecurityRoles.Doctor)
        {
            var harness = ClinicalHarness.ForRole(role);
            var encounter = harness.SeedEncounter(patientId: harness.SeedPatient().Id);
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, ClinicalHarness.Start);
            harness.Time.Now = ClinicalHarness.Start.AddMinutes(30);
            return (harness, encounter);
        }

        private static RecordClinicalNoteRequest Note(DateTimeOffset at) =>
            new(ClinicalNoteType.Evolution, "Paciente estable (dato de prueba).", at);

        [Fact]
        public async Task ValidNoteIsWrittenByTheCurrentStaffAndAudited()
        {
            var (harness, encounter) = InProgress(SecurityRoles.Nurse);

            var note = await harness.Records.RecordNoteAsync(encounter.Id, Note(harness.Time.Now.AddMinutes(-5)), None);

            Assert.Equal((await harness.Actor.GetAsync(None)).StaffId, note.AuthorStaffId);
            Assert.Equal(ClinicalNoteType.Evolution, Assert.Single(harness.Store.Notes).Type);
            Assert.Equal(AuditActionType.Create, Assert.Single(harness.Store.Audit).Action);
            Assert.Contains(encounter.Id, harness.Store.GuardedEncounters);
        }

        [Fact]
        public async Task FirstNoteStartsACreatedEncounter()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Doctor);
            var encounter = harness.SeedEncounter();

            await harness.Records.RecordNoteAsync(encounter.Id, Note(ClinicalHarness.Start), None);

            Assert.Equal(ClinicalEncounterStatus.InProgress, encounter.Status);
        }

        [Fact]
        public async Task NoteTimesMustBeWithinTheEncounter()
        {
            var (harness, encounter) = InProgress();

            var future = await Assert.ThrowsAsync<RequestValidationException>(() =>
                harness.Records.RecordNoteAsync(encounter.Id, Note(harness.Time.Now.AddMinutes(1)), None));
            var early = await Assert.ThrowsAsync<RequestValidationException>(() =>
                harness.Records.RecordNoteAsync(encounter.Id, Note(ClinicalHarness.Start.AddMinutes(-1)), None));
            var blank = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Records.RecordNoteAsync(
                encounter.Id, new RecordClinicalNoteRequest(ClinicalNoteType.Evolution, " ", harness.Time.Now), None));

            Assert.Equal("occurredAtUtc", future.Field);
            Assert.Equal("occurredAtUtc", early.Field);
            Assert.Equal("content", blank.Field);
            Assert.Empty(harness.Store.Notes);
        }

        [Fact]
        public async Task ValidDiagnosisFollowsTheDomainRules()
        {
            var (harness, encounter) = InProgress();

            var definitive = await harness.Records.RecordDiagnosisAsync(encounter.Id,
                new RecordDiagnosisRequest("Neumonía adquirida en la comunidad", DiagnosisType.Definitive, true, "icd-10", "j18.9"), None);
            var presumptive = await harness.Records.RecordDiagnosisAsync(encounter.Id,
                new RecordDiagnosisRequest("Deshidratación leve", DiagnosisType.Presumptive), None);

            Assert.Equal(("ICD-10", "J18.9"), (definitive.CodeSystem, definitive.Code));
            Assert.Equal(harness.Time.Now, definitive.ConfirmedAtUtc);
            Assert.Null(presumptive.ConfirmedAtUtc);
            Assert.Equal(2, harness.Store.Diagnoses.Count);
            Assert.Equal((await harness.Actor.GetAsync(None)).StaffId, definitive.RecordedByStaffId);
        }

        [Fact]
        public async Task ACodeWithoutItsSystemIsInvalid()
        {
            var (harness, encounter) = InProgress();

            var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Records.RecordDiagnosisAsync(
                encounter.Id, new RecordDiagnosisRequest("Cefalea", DiagnosisType.Presumptive, Code: "R51"), None));

            Assert.Equal("codeSystem", error.Field);
        }

        [Fact]
        public async Task MissingEncounterIsNotFound()
        {
            var (harness, _) = InProgress();
            var missing = Guid.NewGuid();

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Records.RecordNoteAsync(missing, Note(harness.Time.Now), None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Records.RecordDiagnosisAsync(
                missing, new RecordDiagnosisRequest("x", DiagnosisType.Presumptive), None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Encounters.CloseAsync(missing, new CloseEncounterRequest(), None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Records.ListNotesAsync(missing, None));
        }

        [Fact]
        public async Task ForeignClinicIsDenied()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Doctor);
            var foreign = harness.SeedEncounter(clinicId: Guid.NewGuid());
            foreign.ChangeStatus(ClinicalEncounterStatus.InProgress, ClinicalHarness.Start);

            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Records.RecordNoteAsync(foreign.Id, Note(ClinicalHarness.Start), None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Records.RecordDiagnosisAsync(
                foreign.Id, new RecordDiagnosisRequest("x", DiagnosisType.Presumptive), None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Encounters.CloseAsync(foreign.Id, new CloseEncounterRequest(), None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Records.ListDiagnosesAsync(foreign.Id, None));
            Assert.Equal(ClinicalEncounterStatus.InProgress, foreign.Status);
        }

        [Fact]
        public async Task UserWithoutClinicalStaffCannotWriteOrClose()
        {
            var harness = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Doctor));
            var encounter = harness.SeedEncounter(clinicId: Guid.NewGuid());

            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Records.RecordNoteAsync(encounter.Id, Note(ClinicalHarness.Start), None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Encounters.CloseAsync(encounter.Id, new CloseEncounterRequest(), None));
        }

        [Fact]
        public async Task CloseEndsTheEncounterWithItsDischargeNoteInOneSave()
        {
            var (harness, encounter) = InProgress();

            await harness.Encounters.CloseAsync(encounter.Id, new CloseEncounterRequest("Alta a domicilio (dato de prueba)."), None);

            Assert.Equal(ClinicalEncounterStatus.Closed, encounter.Status);
            Assert.Equal(harness.Time.Now, encounter.EndedAtUtc);
            Assert.Equal(ClinicalNoteType.Discharge, Assert.Single(harness.Store.Notes).Type);
            Assert.Equal(1, harness.Store.SaveCount);
            var closeAudit = Assert.Single(harness.Store.Audit, a => a.Action == AuditActionType.Update);
            Assert.Contains("Closed", closeAudit.ChangesJson);
        }

        [Fact]
        public async Task CloseWithoutDischargeNoteWritesNoNote()
        {
            var (harness, encounter) = InProgress();

            await harness.Encounters.CloseAsync(encounter.Id, new CloseEncounterRequest(" "), None);

            Assert.Equal(ClinicalEncounterStatus.Closed, encounter.Status);
            Assert.Empty(harness.Store.Notes);
        }

        [Fact]
        public async Task ClosingTwiceOrFromCreatedIsAConflict()
        {
            var (harness, encounter) = InProgress();
            var created = harness.SeedEncounter();

            await harness.Encounters.CloseAsync(encounter.Id, new CloseEncounterRequest(), None);

            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Encounters.CloseAsync(encounter.Id, new CloseEncounterRequest(), None));
            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Encounters.CloseAsync(created.Id, new CloseEncounterRequest(), None));
            Assert.Equal(ClinicalEncounterStatus.Created, created.Status);
        }

        [Fact]
        public async Task NothingIsWrittenOnAClosedEncounter()
        {
            var (harness, encounter) = InProgress();
            await harness.Encounters.CloseAsync(encounter.Id, new CloseEncounterRequest(), None);
            var saves = harness.Store.SaveCount;

            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Records.RecordNoteAsync(encounter.Id, Note(harness.Time.Now), None));
            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Records.RecordDiagnosisAsync(
                encounter.Id, new RecordDiagnosisRequest("x", DiagnosisType.Presumptive), None));
            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Records.RecordTriageAsync(
                encounter.Id, new RecordTriageRequest(TriagePriority.Urgent), None));
            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Records.RecordVitalSignAsync(
                encounter.Id, new RecordVitalSignRequest(VitalSignType.HeartRate, 80, harness.Time.Now), None));

            Assert.Equal(saves, harness.Store.SaveCount);
        }

        [Fact]
        public async Task EveryClinicalWriteReChecksTheEncounterConcurrencyTokens()
        {
            var (harness, encounter) = InProgress(SecurityRoles.Doctor);

            await harness.Records.RecordTriageAsync(encounter.Id, new RecordTriageRequest(TriagePriority.Urgent), None);
            harness.Store.GuardedEncounters.Clear();
            await harness.Records.RecordVitalSignAsync(encounter.Id, new RecordVitalSignRequest(VitalSignType.HeartRate, 80, harness.Time.Now), None);
            Assert.Contains(encounter.Id, harness.Store.GuardedEncounters);
            harness.Store.GuardedEncounters.Clear();
            await harness.Records.RecordDiagnosisAsync(encounter.Id, new RecordDiagnosisRequest("x", DiagnosisType.Presumptive), None);
            Assert.Contains(encounter.Id, harness.Store.GuardedEncounters);
        }
    }
}
