using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Tests.Clinical
{
    // An observation of the paramedic on a prehospital emergency (PrehospitalEmergencyService.RecordNoteAsync): type always
    // Prehospital, author = the session's staff, content 1 to 8000 characters after trimming, one audit entry without the
    // content, idempotent, occurredAt from the start of the emergency up to 5 minutes after the server's clock. CreatedAtUtc
    // comes from the Domain's own clock, so it is never compared exactly.
    public class EmergencyNotesTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private static readonly DateTimeOffset StartedAt = ClinicalHarness.Start.AddMinutes(-10);
        private const string Text = "Paciente consciente, orientado, sin dolor torácico.";

        private static RecordEmergencyNoteRequest Note(DateTimeOffset? occurredAt = null, Guid? clientRequestId = null, string content = Text) =>
            new(clientRequestId ?? Guid.NewGuid(), occurredAt ?? ClinicalHarness.Start, content);

        private static async Task<(ClinicalHarness Harness, ClinicalEncounter Emergency)> OpenedEmergencyAsync(DateTimeOffset? startedAt = null)
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var opened = await harness.Emergencies.OpenAsync(
                new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), startedAt ?? StartedAt, "Síncope"), None);
            return (harness, harness.Store.Encounters.Single(e => e.Id == opened.Emergency.Id));
        }

        private static List<ClinicalNote> Notes(ClinicalHarness harness) => harness.Store.Notes;

        private static int NoteAudits(ClinicalHarness harness) =>
            harness.Store.Audit.Count(a => a.Action == AuditActionType.Create && a.EntityName == nameof(ClinicalNote));

        [Fact]
        public async Task APrehospitalNoteIsRecordedOnTheNnEmergencyWithItsAuditAndReceipt()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var request = Note(ClinicalHarness.Start.AddMinutes(-2), content: $"  {Text}  ");

            var result = await harness.Emergencies.RecordNoteAsync(emergency.Id, request, None);

            Assert.True(result.Created);
            var staffId = (await harness.Actor.GetAsync(None)).StaffId;
            var note = Assert.Single(Notes(harness));
            Assert.Equal((emergency.Id, staffId, ClinicalNoteType.Prehospital, Text, request.OccurredAt),
                (note.ClinicalEncounterId, note.AuthorStaffId, note.Type, note.Content, note.OccurredAtUtc));

            var audit = Assert.Single(harness.Store.Audit, a => a.EntityName == nameof(ClinicalNote));
            Assert.Equal((AuditActionType.Create, note.Id.ToString(), emergency.Id, (Guid?)null),
                (audit.Action, audit.EntityId, audit.ClinicalEncounterId, audit.PatientId));
            // The audit never carries the clinical text.
            Assert.Null(audit.ChangesJson);

            var receipt = harness.Store.Receipts.Single(r => r.Key.Operation == PrehospitalEmergencyService.RecordNoteOperation);
            Assert.Equal(new IdempotencyKey(harness.User.UserId!.Value, PrehospitalEmergencyService.RecordNoteOperation, request.ClientRequestId), receipt.Key);
            Assert.Equal(note.Id, receipt.ResourceId);
            Assert.Equal(new EmergencyNoteSummary(note.Id, emergency.Id, ClinicalNoteType.Prehospital, Text, request.OccurredAt), result.Note);
            Assert.Contains(emergency.Id, harness.Store.GuardedEncounters);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\n\t")]
        public async Task AMissingContentIsRejected(string? content)
        {
            var (harness, emergency) = await OpenedEmergencyAsync();

            var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordNoteAsync(
                emergency.Id, Note(content: content!), None));

            Assert.Equal("content", error.Field);
            Assert.Empty(Notes(harness));
        }

        // 8000 characters (the Domain limit) after trimming; the error never echoes the content.
        [Fact]
        public async Task TheContentGoesUpToTheDomainLimit()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();

            await harness.Emergencies.RecordNoteAsync(emergency.Id, Note(content: $" {new string('a', FieldLengths.NoteContent)} "), None);
            var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordNoteAsync(
                emergency.Id, Note(content: new string('a', FieldLengths.NoteContent + 1)), None));

            Assert.Equal("content", error.Field);
            Assert.DoesNotContain("aaaa", error.Message);
            Assert.Equal(FieldLengths.NoteContent, Assert.Single(Notes(harness)).Content.Length);
        }

        [Fact]
        public async Task AMissingKeyIsRejected()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();

            var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordNoteAsync(
                emergency.Id, Note(clientRequestId: Guid.Empty), None));

            Assert.Equal("clientRequestId", error.Field);
        }

        [Fact]
        public async Task AnIdentifiedEmergencyRecordsItsPatientInTheAudit()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var patient = Patient.Register("Ana", "Pérez", new DateOnly(1980, 1, 1), BiologicalSex.Female);
            patient.AddIdentifier(PatientIdentifierType.NationalId, "001-1234567-8", PatientIdentifierSource.Manual, isPrimary: true);
            harness.Store.Patients.Add(patient);
            await harness.Documents.ConfirmAsync(emergency.Id,
                new IdentifyEmergencyByDocumentRequest(Guid.NewGuid(), PatientIdentifierType.NationalId, "001-1234567-8"), None);

            await harness.Emergencies.RecordNoteAsync(emergency.Id, Note(), None);

            Assert.Equal(patient.Id, Assert.Single(harness.Store.Audit, a => a.EntityName == nameof(ClinicalNote)).PatientId);
        }

        [Fact]
        public async Task TheFirstNoteStartsTheEmergencyOnce()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var request = Note();

            await harness.Emergencies.RecordNoteAsync(emergency.Id, request, None);
            Assert.Equal(ClinicalEncounterStatus.InProgress, emergency.Status);
            var updatedAt = emergency.UpdatedAtUtc;
            var saves = harness.Store.SaveCount;

            var replay = await harness.Emergencies.RecordNoteAsync(emergency.Id, request, None);
            await harness.Emergencies.RecordNoteAsync(emergency.Id, Note(content: "Segunda observación."), None);

            Assert.False(replay.Created);
            Assert.Equal((ClinicalEncounterStatus.InProgress, updatedAt), (emergency.Status, emergency.UpdatedAtUtc));
            Assert.Equal(saves + 1, harness.Store.SaveCount);
            Assert.Equal(2, Notes(harness).Count);
        }

        [Theory]
        [InlineData(-600_000, true)]          // == startedAt
        [InlineData(-600_001, false)]         // before startedAt
        [InlineData(0, true)]                 // == now
        [InlineData(300_000, true)]           // now + 5 min
        [InlineData(300_001, false)]          // now + 5 min + 1 ms
        public async Task OccurredAtGoesFromTheStartToFiveMinutesAhead(long offsetMilliseconds, bool accepted)
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var request = Note(ClinicalHarness.Start.AddMilliseconds(offsetMilliseconds));

            if (accepted)
            {
                await harness.Emergencies.RecordNoteAsync(emergency.Id, request, None);
                Assert.Single(Notes(harness));
            }
            else
            {
                var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordNoteAsync(emergency.Id, request, None));
                Assert.Equal("occurredAt", error.Field);
                Assert.Empty(Notes(harness));
                Assert.Equal(ClinicalEncounterStatus.Created, emergency.Status);
            }
        }

        [Fact]
        public async Task OccurredAtIsStoredInUtcAndTheToleratedFutureStartIsAccepted()
        {
            var (harness, emergency) = await OpenedEmergencyAsync(startedAt: ClinicalHarness.Start.AddMinutes(4));
            var local = ClinicalHarness.Start.AddMinutes(4.5).ToOffset(TimeSpan.FromHours(-4));

            var result = await harness.Emergencies.RecordNoteAsync(emergency.Id, Note(local), None);

            Assert.Equal(TimeSpan.Zero, result.Note.OccurredAtUtc.Offset);
            Assert.Equal(TimeSpan.Zero, Assert.Single(Notes(harness)).OccurredAtUtc.Offset);
            Assert.Equal(ClinicalEncounterStatus.InProgress, emergency.Status);
        }

        // Same key + same note (another offset, other surrounding spaces): the same answer, nothing new, even after the
        // clock moved on.
        [Fact]
        public async Task RetryingReplaysTheNoteWithoutNewRowsOrAudits()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var key = Guid.NewGuid();
            var first = await harness.Emergencies.RecordNoteAsync(emergency.Id, Note(clientRequestId: key), None);
            var audits = harness.Store.Audit.Count;
            harness.Time.Now = ClinicalHarness.Start.AddHours(30);

            var replay = await harness.Emergencies.RecordNoteAsync(emergency.Id, Note(
                ClinicalHarness.Start.ToOffset(TimeSpan.FromHours(-4)), key, $"\n {Text} "), None);

            Assert.False(replay.Created);
            Assert.Equal(first.Note, replay.Note);
            Assert.Single(Notes(harness));
            Assert.Equal(audits, harness.Store.Audit.Count);
        }

        [Fact]
        public async Task TheSameKeyWithAnotherContentTimeOrEmergencyIsAConflict()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var other = await harness.Emergencies.OpenAsync(new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), StartedAt, "Caída"), None);
            var key = Guid.NewGuid();
            await harness.Emergencies.RecordNoteAsync(emergency.Id, Note(clientRequestId: key), None);
            var original = Assert.Single(Notes(harness));

            foreach (var (emergencyId, request) in new[]
            {
                (emergency.Id, Note(clientRequestId: key, content: Text + " Editado.")),
                (emergency.Id, Note(ClinicalHarness.Start.AddSeconds(-1), key)),
                (other.Emergency.Id, Note(clientRequestId: key))
            })
            {
                await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.RecordNoteAsync(emergencyId, request, None));
            }

            Assert.Same(original, Assert.Single(Notes(harness)));
            Assert.Equal(Text, original.Content);
        }

        [Fact]
        public async Task LosingARaceWithTheSameNoteReturnsTheWinner()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var request = Note();
            var key = new IdempotencyKey(harness.User.UserId!.Value, PrehospitalEmergencyService.RecordNoteOperation, request.ClientRequestId);
            var hash = IdempotencyRequestHash.Compute(emergency.Id.ToString("D"), IdempotencyRequestHash.Instant(request.OccurredAt), Text);
            var winnerId = Guid.NewGuid();
            harness.Store.BeforeSave = () =>
            {
                harness.Store.BeforeSave = null;
                harness.Store.Receipts.Add(new IdempotencyReceipt(key, hash, winnerId, ClinicalHarness.Start));
            };

            var result = await harness.Emergencies.RecordNoteAsync(emergency.Id, request, None);

            Assert.False(result.Created);
            Assert.Equal(winnerId, result.Note.Id);
            Assert.Empty(Notes(harness));
            Assert.Equal(0, NoteAudits(harness));
        }

        [Fact]
        public async Task AReplayStillChecksAccess()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var request = Note();
            await harness.Emergencies.RecordNoteAsync(emergency.Id, request, None);
            harness.Store.Assignments.Single(a => a.ClinicalEncounterId == emergency.Id).Unassign(ClinicalHarness.Start.AddMinutes(1));

            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Emergencies.RecordNoteAsync(emergency.Id, request, None));
        }

        [Fact]
        public async Task OnlyTheAssignedParamedicRecordsOnThePrehospitalEmergency()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var other = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic), harness.Store);
            other.Staff.Add(other.User.UserId!.Value, true, Guid.NewGuid());

            await Assert.ThrowsAsync<AccessDeniedException>(() => other.Emergencies.RecordNoteAsync(emergency.Id, Note(), None));
            Assert.Empty(Notes(harness));
        }

        [Fact]
        public async Task AnUnknownOrNonPrehospitalEncounterIsNotAnEmergency()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var hospital = harness.SeedEncounter();

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Emergencies.RecordNoteAsync(hospital.Id, Note(), None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Emergencies.RecordNoteAsync(Guid.NewGuid(), Note(), None));
            Assert.Empty(Notes(harness));
        }

        // Transferred without clinic is the way to the hospital and accepts records (EmergencyTransferTests).
        [Theory]
        [InlineData(ClinicalEncounterStatus.Closed)]
        [InlineData(ClinicalEncounterStatus.Cancelled)]
        public async Task AnEmergencyThatNoLongerAcceptsRecordsRejectsTheNote(ClinicalEncounterStatus status)
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            if (status != ClinicalEncounterStatus.Cancelled)
            {
                emergency.ChangeStatus(ClinicalEncounterStatus.InProgress, ClinicalHarness.Start);
            }

            emergency.ChangeStatus(status, ClinicalHarness.Start);

            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.RecordNoteAsync(emergency.Id, Note(), None));
            Assert.Empty(Notes(harness));
            Assert.Equal(status, emergency.Status);
        }

        // Regression: the Web note keeps the clinic rule and its no-future rule.
        [Fact]
        public async Task TheWebNoteKeepsTheClinicAndNoFutureRules()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();

            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Records.RecordNoteAsync(emergency.Id,
                new RecordClinicalNoteRequest(ClinicalNoteType.Prehospital, Text, ClinicalHarness.Start), None));

            var hospital = harness.SeedEncounter();
            var future = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Records.RecordNoteAsync(hospital.Id,
                new RecordClinicalNoteRequest(ClinicalNoteType.Evolution, Text, ClinicalHarness.Start.AddMinutes(1)), None));
            Assert.Equal("occurredAtUtc", future.Field);
            Assert.Empty(Notes(harness));
        }
    }
}
