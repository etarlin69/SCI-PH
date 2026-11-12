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
    // A completed procedure on a prehospital emergency (PrehospitalEmergencyService.RecordProcedureAsync): a code of the
    // fixed server catalog, one instant (performedAt = completedAt), an optional outcome, one audit entry, idempotent,
    // performedAt from the start of the emergency up to 5 minutes after the server's clock.
    public class EmergencyProceduresTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private static readonly DateTimeOffset StartedAt = ClinicalHarness.Start.AddMinutes(-10);

        private static RecordEmergencyProcedureRequest Oxygen(
            DateTimeOffset? performedAt = null, Guid? clientRequestId = null, string? outcome = "SpO2 97 % tras 5 min") =>
            new(clientRequestId ?? Guid.NewGuid(), "OXYGEN-THERAPY", performedAt ?? ClinicalHarness.Start, outcome);

        private static async Task<(ClinicalHarness Harness, ClinicalEncounter Emergency)> OpenedEmergencyAsync(DateTimeOffset? startedAt = null)
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var opened = await harness.Emergencies.OpenAsync(
                new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), startedAt ?? StartedAt, "Disnea"), None);
            return (harness, harness.Store.Encounters.Single(e => e.Id == opened.Emergency.Id));
        }

        private static List<ClinicalProcedure> Procedures(ClinicalHarness harness) => harness.Store.Procedures;

        private static int ProcedureAudits(ClinicalHarness harness) =>
            harness.Store.Audit.Count(a => a.Action == AuditActionType.Create && a.EntityName == nameof(ClinicalProcedure));

        public static TheoryData<string> CatalogCodes() => new(PrehospitalProcedureCatalog.Entries.Select(e => e.Code));

        [Fact]
        public async Task ACatalogProcedureIsRecordedCompletedOnTheNnEmergencyWithItsAuditAndReceipt()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var request = Oxygen(ClinicalHarness.Start.AddMinutes(-2));

            var result = await harness.Emergencies.RecordProcedureAsync(emergency.Id, request, None);

            Assert.True(result.Created);
            Assert.Null(emergency.PatientId);
            var staffId = (await harness.Actor.GetAsync(None)).StaffId;
            var entry = PrehospitalProcedureCatalog.Find("OXYGEN-THERAPY")!;
            var procedure = Assert.Single(Procedures(harness));
            Assert.Equal(
                (emergency.Id, entry.Name, entry.Description, PrehospitalProcedureCatalog.CodeSystem, "OXYGEN-THERAPY", ClinicalProcedureStatus.Completed),
                (procedure.ClinicalEncounterId, procedure.Name, procedure.Description, procedure.CodeSystem, procedure.Code, procedure.Status));
            Assert.Equal(((Guid?)staffId, (DateTimeOffset?)request.PerformedAt, (DateTimeOffset?)request.PerformedAt, "SpO2 97 % tras 5 min"),
                (procedure.PerformedByStaffId, procedure.PerformedAtUtc, procedure.CompletedAtUtc, procedure.Outcome));

            var audit = Assert.Single(harness.Store.Audit, a => a.EntityName == nameof(ClinicalProcedure));
            Assert.Equal((AuditActionType.Create, procedure.Id.ToString(), emergency.Id, (Guid?)null),
                (audit.Action, audit.EntityId, audit.ClinicalEncounterId, audit.PatientId));

            var receipt = harness.Store.Receipts.Single(r => r.Key.Operation == PrehospitalEmergencyService.RecordProcedureOperation);
            Assert.Equal(new IdempotencyKey(harness.User.UserId!.Value, PrehospitalEmergencyService.RecordProcedureOperation, request.ClientRequestId), receipt.Key);
            Assert.Equal(procedure.Id, receipt.ResourceId);

            Assert.Equal(new EmergencyProcedureSummary(procedure.Id, emergency.Id, "OXYGEN-THERAPY", entry.Name, request.PerformedAt, "SpO2 97 % tras 5 min"),
                result.Procedure);
            Assert.Contains(emergency.Id, harness.Store.GuardedEncounters);
        }

        // Every catalog entry is a valid Domain procedure (name, description and code lengths).
        [Theory]
        [MemberData(nameof(CatalogCodes))]
        public async Task EveryCatalogCodeCanBeRecorded(string code)
        {
            var (harness, emergency) = await OpenedEmergencyAsync();

            await harness.Emergencies.RecordProcedureAsync(emergency.Id, Oxygen() with { ProcedureCode = code }, None);

            Assert.Equal(code, Assert.Single(Procedures(harness)).Code);
        }

        [Fact]
        public async Task TheCatalogHasStableUniqueUpperCaseCodesAndNeedsClinicalStaff()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);

            var catalog = await harness.Emergencies.ListProcedureCatalogAsync(None);

            Assert.Equal(["IMMOBILIZATION", "HEMORRHAGE-CONTROL", "OXYGEN-THERAPY", "PERIPHERAL-IV-ACCESS", "CPR"], catalog.Select(e => e.Code));
            Assert.Equal(catalog.Count, catalog.Select(e => e.Code).Distinct().Count());
            Assert.All(catalog, e => Assert.Equal(e.Code.ToUpperInvariant(), e.Code));
            Assert.All(catalog, e => Assert.False(string.IsNullOrWhiteSpace(e.Name)));
            Assert.All(catalog, e => Assert.Same(e, PrehospitalProcedureCatalog.Find(e.Code)));

            var noStaff = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic));
            await Assert.ThrowsAsync<AccessDeniedException>(() => noStaff.Emergencies.ListProcedureCatalogAsync(None));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        public async Task TheOutcomeIsOptional(string? outcome)
        {
            var (harness, emergency) = await OpenedEmergencyAsync();

            var result = await harness.Emergencies.RecordProcedureAsync(emergency.Id, Oxygen(outcome: outcome), None);

            Assert.Null(result.Procedure.Outcome);
            Assert.Null(Assert.Single(Procedures(harness)).Outcome);
        }

        [Fact]
        public async Task TheOutcomeIsTrimmedAndLimitedToTheDomainLength()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();

            await harness.Emergencies.RecordProcedureAsync(emergency.Id, Oxygen(outcome: "  Sin complicaciones  "), None);
            await harness.Emergencies.RecordProcedureAsync(emergency.Id, Oxygen(outcome: new string('a', FieldLengths.LongText)), None);
            var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordProcedureAsync(
                emergency.Id, Oxygen(outcome: new string('a', FieldLengths.LongText + 1)), None));

            Assert.Equal("outcome", error.Field);
            Assert.DoesNotContain("aaaa", error.Message);
            Assert.Equal(["Sin complicaciones", new string('a', FieldLengths.LongText)], Procedures(harness).Select(p => p.Outcome));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("UNKNOWN")]
        [InlineData("PROC-001")]
        [InlineData("oxygen-therapy")]
        [InlineData(" OXYGEN-THERAPY")]
        public async Task ACodeOutsideTheCatalogIsRejectedWithoutWritingAnything(string? code)
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var audits = harness.Store.Audit.Count;

            var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordProcedureAsync(
                emergency.Id, Oxygen() with { ProcedureCode = code! }, None));

            Assert.Equal("procedureCode", error.Field);
            Assert.Empty(Procedures(harness));
            Assert.Equal(audits, harness.Store.Audit.Count);
            Assert.DoesNotContain(harness.Store.Receipts, r => r.Key.Operation == PrehospitalEmergencyService.RecordProcedureOperation);
            Assert.Equal(ClinicalEncounterStatus.Created, emergency.Status);
        }

        [Fact]
        public async Task AMissingKeyIsRejected()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();

            var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordProcedureAsync(
                emergency.Id, Oxygen(clientRequestId: Guid.Empty), None));

            Assert.Equal("clientRequestId", error.Field);
            Assert.Empty(Procedures(harness));
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

            await harness.Emergencies.RecordProcedureAsync(emergency.Id, Oxygen(), None);

            Assert.Equal(patient.Id, emergency.PatientId);
            Assert.Equal(patient.Id, Assert.Single(harness.Store.Audit, a => a.EntityName == nameof(ClinicalProcedure)).PatientId);
        }

        // The procedure can be the first clinical record of the emergency: it starts it, once.
        [Fact]
        public async Task TheFirstProcedureStartsTheEmergencyOnce()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            Assert.Equal(ClinicalEncounterStatus.Created, emergency.Status);
            var request = Oxygen();

            await harness.Emergencies.RecordProcedureAsync(emergency.Id, request, None);
            Assert.Equal(ClinicalEncounterStatus.InProgress, emergency.Status);
            var updatedAt = emergency.UpdatedAtUtc;
            var saves = harness.Store.SaveCount;

            var replay = await harness.Emergencies.RecordProcedureAsync(emergency.Id, request, None);
            await harness.Emergencies.RecordProcedureAsync(emergency.Id, Oxygen() with { ProcedureCode = "CPR" }, None);

            Assert.False(replay.Created);
            Assert.Equal(ClinicalEncounterStatus.InProgress, emergency.Status);
            Assert.Equal(updatedAt, emergency.UpdatedAtUtc);
            Assert.Equal(saves + 1, harness.Store.SaveCount);
        }

        // performedAt: from the start of the emergency up to now + 5 minutes, both inclusive.
        [Theory]
        [InlineData(-600_000, true)]          // == startedAt
        [InlineData(-600_001, false)]         // before startedAt
        [InlineData(0, true)]                 // == now
        [InlineData(300_000, true)]           // now + 5 min
        [InlineData(300_001, false)]          // now + 5 min + 1 ms
        public async Task PerformedAtGoesFromTheStartToFiveMinutesAhead(long offsetMilliseconds, bool accepted)
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var request = Oxygen(ClinicalHarness.Start.AddMilliseconds(offsetMilliseconds));

            if (accepted)
            {
                await harness.Emergencies.RecordProcedureAsync(emergency.Id, request, None);
                Assert.Single(Procedures(harness));
            }
            else
            {
                var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RecordProcedureAsync(emergency.Id, request, None));
                Assert.Equal("performedAt", error.Field);
                Assert.Empty(Procedures(harness));
                Assert.Equal(ClinicalEncounterStatus.Created, emergency.Status);
            }
        }

        // The device clock 4 minutes ahead: the emergency starts "in the future" and its first procedure too. Both accepted,
        // and the transition time is not earlier than the start.
        [Fact]
        public async Task AProcedureRightAfterAnEmergencyStartedInTheToleratedFutureIsAccepted()
        {
            var (harness, emergency) = await OpenedEmergencyAsync(startedAt: ClinicalHarness.Start.AddMinutes(4));

            await harness.Emergencies.RecordProcedureAsync(emergency.Id, Oxygen(ClinicalHarness.Start.AddMinutes(4.5)), None);

            Assert.Single(Procedures(harness));
            Assert.Equal(ClinicalEncounterStatus.InProgress, emergency.Status);
        }

        [Fact]
        public async Task PerformedAtIsStoredInUtc()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var local = ClinicalHarness.Start.ToOffset(TimeSpan.FromHours(-4));

            var result = await harness.Emergencies.RecordProcedureAsync(emergency.Id, Oxygen(local), None);

            Assert.Equal(TimeSpan.Zero, result.Procedure.PerformedAtUtc.Offset);
            var procedure = Assert.Single(Procedures(harness));
            Assert.Equal((TimeSpan.Zero, TimeSpan.Zero), (procedure.PerformedAtUtc!.Value.Offset, procedure.CompletedAtUtc!.Value.Offset));
        }

        // Same key + same procedure (the same instant in another offset, the outcome with other spaces): the same answer,
        // nothing new. Even after the clock moved on.
        [Fact]
        public async Task RetryingReplaysTheProcedureWithoutNewRowsOrAudits()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var key = Guid.NewGuid();
            var first = await harness.Emergencies.RecordProcedureAsync(emergency.Id, Oxygen(clientRequestId: key), None);
            var audits = harness.Store.Audit.Count;
            harness.Time.Now = ClinicalHarness.Start.AddHours(30);

            var replay = await harness.Emergencies.RecordProcedureAsync(emergency.Id, Oxygen(
                ClinicalHarness.Start.ToOffset(TimeSpan.FromHours(-4)), key, "  SpO2 97 % tras 5 min "), None);

            Assert.False(replay.Created);
            Assert.Equal(first.Procedure, replay.Procedure);
            Assert.Single(Procedures(harness));
            Assert.Equal(audits, harness.Store.Audit.Count);
        }

        [Fact]
        public async Task TheSameKeyWithAnotherProcedureTimeOutcomeOrEmergencyIsAConflict()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var other = await harness.Emergencies.OpenAsync(new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), StartedAt, "Caída"), None);
            var key = Guid.NewGuid();
            await harness.Emergencies.RecordProcedureAsync(emergency.Id, Oxygen(clientRequestId: key), None);
            var original = Assert.Single(Procedures(harness));

            foreach (var (emergencyId, request) in new[]
            {
                (emergency.Id, Oxygen(clientRequestId: key) with { ProcedureCode = "CPR" }),
                (emergency.Id, Oxygen(ClinicalHarness.Start.AddSeconds(-1), key)),
                (emergency.Id, Oxygen(clientRequestId: key, outcome: "Otro resultado")),
                (emergency.Id, Oxygen(clientRequestId: key, outcome: null)),
                (other.Emergency.Id, Oxygen(clientRequestId: key))
            })
            {
                await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.RecordProcedureAsync(emergencyId, request, None));
            }

            Assert.Same(original, Assert.Single(Procedures(harness)));
            Assert.Equal(("OXYGEN-THERAPY", "SpO2 97 % tras 5 min"), (original.Code, original.Outcome));
        }

        // Two procedures with different keys are two procedures, even the same one at the same instant.
        [Fact]
        public async Task DifferentKeysAreDifferentProcedures()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();

            await harness.Emergencies.RecordProcedureAsync(emergency.Id, Oxygen(), None);
            await harness.Emergencies.RecordProcedureAsync(emergency.Id, Oxygen(), None);

            Assert.Equal(2, Procedures(harness).Count);
            Assert.Equal(2, ProcedureAudits(harness));
        }

        // The same procedure lost the race to itself: the winner's receipt answers it, nothing is written twice.
        [Fact]
        public async Task LosingARaceWithTheSameProcedureReturnsTheWinner()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var request = Oxygen();
            var key = new IdempotencyKey(harness.User.UserId!.Value, PrehospitalEmergencyService.RecordProcedureOperation, request.ClientRequestId);
            var hash = IdempotencyRequestHash.Compute(
                emergency.Id.ToString("D"), "OXYGEN-THERAPY", IdempotencyRequestHash.Instant(request.PerformedAt), "SpO2 97 % tras 5 min");
            var winnerId = Guid.NewGuid();
            harness.Store.BeforeSave = () =>
            {
                harness.Store.BeforeSave = null;
                harness.Store.Receipts.Add(new IdempotencyReceipt(key, hash, winnerId, ClinicalHarness.Start));
            };

            var result = await harness.Emergencies.RecordProcedureAsync(emergency.Id, request, None);

            Assert.False(result.Created);
            Assert.Equal(winnerId, result.Procedure.Id);
            Assert.Empty(Procedures(harness));
            Assert.Equal(0, ProcedureAudits(harness));
        }

        // Another request with the same key, but other data, won the race: this one is a conflict.
        [Fact]
        public async Task LosingARaceToAnotherProcedureWithTheSameKeyIsAConflict()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var request = Oxygen();
            var key = new IdempotencyKey(harness.User.UserId!.Value, PrehospitalEmergencyService.RecordProcedureOperation, request.ClientRequestId);
            harness.Store.BeforeSave = () =>
            {
                harness.Store.BeforeSave = null;
                harness.Store.Receipts.Add(new IdempotencyReceipt(key, "another-hash", Guid.NewGuid(), ClinicalHarness.Start));
            };

            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.RecordProcedureAsync(emergency.Id, request, None));

            Assert.Empty(Procedures(harness));
        }

        [Fact]
        public async Task AReplayStillChecksAccess()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var request = Oxygen();
            await harness.Emergencies.RecordProcedureAsync(emergency.Id, request, None);
            harness.Store.Assignments.Single(a => a.ClinicalEncounterId == emergency.Id).Unassign(ClinicalHarness.Start.AddMinutes(1));

            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Emergencies.RecordProcedureAsync(emergency.Id, request, None));
        }

        [Fact]
        public async Task OnlyTheAssignedParamedicRecordsOnThePrehospitalEmergency()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var other = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic), harness.Store);
            other.Staff.Add(other.User.UserId!.Value, true, Guid.NewGuid());

            await Assert.ThrowsAsync<AccessDeniedException>(() => other.Emergencies.RecordProcedureAsync(emergency.Id, Oxygen(), None));
            Assert.Empty(Procedures(harness));
        }

        [Fact]
        public async Task AnUnknownOrNonPrehospitalEncounterIsNotAnEmergency()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var hospital = harness.SeedEncounter();

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Emergencies.RecordProcedureAsync(hospital.Id, Oxygen(), None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Emergencies.RecordProcedureAsync(Guid.NewGuid(), Oxygen(), None));
            Assert.Empty(Procedures(harness));
        }

        // Transferred without clinic is the way to the hospital and accepts records (EmergencyTransferTests).
        [Theory]
        [InlineData(ClinicalEncounterStatus.Closed)]
        [InlineData(ClinicalEncounterStatus.Cancelled)]
        public async Task AnEmergencyThatNoLongerAcceptsRecordsRejectsTheProcedure(ClinicalEncounterStatus status)
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            if (status != ClinicalEncounterStatus.Cancelled)
            {
                emergency.ChangeStatus(ClinicalEncounterStatus.InProgress, ClinicalHarness.Start);
            }

            emergency.ChangeStatus(status, ClinicalHarness.Start);

            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.RecordProcedureAsync(emergency.Id, Oxygen(), None));
            Assert.Empty(Procedures(harness));
            Assert.Equal(status, emergency.Status);
        }
    }
}
