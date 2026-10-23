using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;

namespace SCI_PH.Application.Tests.Clinical
{
    public class PrehospitalEmergencyTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        private static OpenPrehospitalEmergencyRequest Request(Guid? clientRequestId = null, DateTimeOffset? startedAt = null, string chiefComplaint = "Dolor torácico") =>
            new(clientRequestId ?? Guid.NewGuid(), startedAt ?? ClinicalHarness.Start.AddMinutes(-10), chiefComplaint);

        [Fact]
        public async Task OpensAPrehospitalNnEncounterWithoutClinicAssignedToTheParamedic()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var startedAt = ClinicalHarness.Start.AddMinutes(-12);
            var request = Request(startedAt: startedAt, chiefComplaint: "  Disnea súbita  ");

            var result = await harness.Emergencies.OpenAsync(request, None);

            Assert.True(result.Created);
            var encounter = Assert.Single(harness.Store.Encounters);
            Assert.Equal(encounter.Id, result.Emergency.Id);
            Assert.Equal(ClinicalEncounterType.Prehospital, encounter.Type);
            Assert.Null(encounter.ClinicId);
            Assert.Null(encounter.PatientId);
            Assert.Null(encounter.AmbulanceId);
            Assert.Matches("^NN-[0-9A-F]{32}$", encounter.TemporaryPatientCode!.Value);
            Assert.Equal("Disnea súbita", encounter.Reason);
            Assert.Equal(startedAt, encounter.StartedAtUtc);
            Assert.Equal(ClinicalEncounterStatus.Created, encounter.Status);

            var staffId = (await harness.Actor.GetAsync(None)).StaffId;
            var assignment = Assert.Single(harness.Store.Assignments);
            Assert.Equal((encounter.Id, staffId, EncounterStaffFunction.Paramedic, true),
                (assignment.ClinicalEncounterId, assignment.ClinicalStaffId, assignment.Function, assignment.IsActive));

            var audit = Assert.Single(harness.Store.Audit);
            Assert.Equal((AuditActionType.Create, nameof(ClinicalEncounter), encounter.Id.ToString(), encounter.Id),
                (audit.Action, audit.EntityName, audit.EntityId, audit.ClinicalEncounterId));
            Assert.Equal(harness.User.UserId, audit.ActorIdentityUserId);

            var receipt = Assert.Single(harness.Store.Receipts);
            Assert.Equal(new IdempotencyKey(harness.User.UserId!.Value, PrehospitalEmergencyService.OpenOperation, request.ClientRequestId), receipt.Key);
            Assert.Equal(encounter.Id, receipt.ResourceId);
            Assert.Matches("^[0-9a-f]{64}$", receipt.RequestHash);
            Assert.Equal(1, harness.Store.SaveCount);

            Assert.Equal(encounter.TemporaryPatientCode.Value, result.Emergency.TemporaryPatientCode);
            Assert.Equal("Disnea súbita", result.Emergency.ChiefComplaint);
            Assert.Null(result.Emergency.Patient);
        }

        // Bounds are inclusive: up to 5 minutes ahead (device clock skew) and up to 24 hours back.
        [Theory]
        [InlineData(0, true)]
        [InlineData(299_000, true)]
        [InlineData(300_000, true)]
        [InlineData(300_001, false)]
        [InlineData(-86_340_000, true)]
        [InlineData(-86_400_000, true)]
        [InlineData(-86_400_001, false)]
        public async Task StartedAtMustBeWithinFiveMinutesAheadAndADayBack(long offsetMilliseconds, bool accepted)
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var request = Request(startedAt: ClinicalHarness.Start.AddMilliseconds(offsetMilliseconds));

            if (accepted)
            {
                var result = await harness.Emergencies.OpenAsync(request, None);
                Assert.Equal(request.StartedAt, result.Emergency.StartedAtUtc);
                return;
            }

            var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.OpenAsync(request, None));
            Assert.Equal("startedAt", error.Field);
            Assert.Empty(harness.Store.Encounters);
            Assert.Empty(harness.Store.Receipts);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task AChiefComplaintIsRequired(string chiefComplaint)
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);

            var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.OpenAsync(Request(chiefComplaint: chiefComplaint), None));
            Assert.Equal("chiefComplaint", error.Field);
        }

        [Fact]
        public async Task AClientRequestIdIsRequired()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);

            var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.OpenAsync(Request(clientRequestId: Guid.Empty), None));
            Assert.Equal("clientRequestId", error.Field);
        }

        [Fact]
        public async Task RetryingTheSameRequestReturnsTheSameEmergency()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var request = Request();

            var first = await harness.Emergencies.OpenAsync(request, None);
            // Same instant written with another offset and extra spaces: the same logical request.
            var again = await harness.Emergencies.OpenAsync(
                request with { StartedAt = request.StartedAt.ToOffset(TimeSpan.FromHours(-4)), ChiefComplaint = $" {request.ChiefComplaint} " }, None);

            Assert.True(first.Created);
            Assert.False(again.Created);
            Assert.Equal(first.Emergency, again.Emergency);
            Assert.Single(harness.Store.Encounters);
            Assert.Single(harness.Store.Assignments);
            Assert.Single(harness.Store.Audit);
            Assert.Single(harness.Store.Receipts);
        }

        [Fact]
        public async Task AReplayIsAnsweredEvenAfterStartedAtLeftTheWindow()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var request = Request(startedAt: ClinicalHarness.Start.AddHours(-23));
            var first = await harness.Emergencies.OpenAsync(request, None);

            harness.Time.Now = ClinicalHarness.Start.AddHours(2);
            var again = await harness.Emergencies.OpenAsync(request, None);

            Assert.Equal(first.Emergency.Id, again.Emergency.Id);
            Assert.False(again.Created);
        }

        [Fact]
        public async Task TheSameKeyWithOtherDataIsAConflict()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var request = Request();
            await harness.Emergencies.OpenAsync(request, None);

            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.OpenAsync(request with { ChiefComplaint = "Otra cosa" }, None));
            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.OpenAsync(request with { StartedAt = request.StartedAt.AddSeconds(1) }, None));

            Assert.Single(harness.Store.Encounters);
            Assert.Single(harness.Store.Receipts);
            Assert.Equal(1, harness.Store.SaveCount);
        }

        [Fact]
        public async Task TheSameKeyFromAnotherUserIsAnotherRequest()
        {
            var store = new FakeClinicalStore();
            var first = Paramedic(store);
            var second = Paramedic(store);
            var request = Request();

            var a = await first.Emergencies.OpenAsync(request, None);
            var b = await second.Emergencies.OpenAsync(request, None);

            Assert.NotEqual(a.Emergency.Id, b.Emergency.Id);
            Assert.True(b.Created);
            Assert.Equal(2, store.Receipts.Count);
        }

        // The competing request commits its receipt after this one checked for it: the unique key rejects this save,
        // nothing of it is kept, and it answers with the winner's emergency.
        [Fact]
        public async Task LosingARaceWithTheSameRequestReturnsTheWinner()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var request = Request();
            var winner = await harness.Emergencies.OpenAsync(request, None);
            var winnerReceipt = harness.Store.Receipts.Single();
            harness.Store.Receipts.Clear();
            harness.Store.BeforeSave = () => harness.Store.Receipts.Add(winnerReceipt);

            var loser = await harness.Emergencies.OpenAsync(request, None);

            Assert.False(loser.Created);
            Assert.Equal(winner.Emergency.Id, loser.Emergency.Id);
            Assert.Single(harness.Store.Encounters);
            Assert.Single(harness.Store.Assignments);
            Assert.Single(harness.Store.Audit);
            Assert.Single(harness.Store.Receipts);
        }

        [Fact]
        public async Task LosingARaceWithOtherDataIsAConflict()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var request = Request();
            await harness.Emergencies.OpenAsync(request, None);
            var winnerReceipt = harness.Store.Receipts.Single();
            harness.Store.Receipts.Clear();
            harness.Store.BeforeSave = () => harness.Store.Receipts.Add(winnerReceipt);

            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.OpenAsync(request with { ChiefComplaint = "Otra cosa" }, None));
            Assert.Single(harness.Store.Encounters);
        }

        [Fact]
        public async Task WithoutClinicalStaffNothingIsCreated()
        {
            var harness = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic));

            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Emergencies.OpenAsync(Request(), None));
            Assert.Empty(harness.Store.Encounters);
            Assert.Empty(harness.Store.Receipts);
            Assert.Equal(0, harness.Store.SaveCount);
        }

        [Fact]
        public async Task WithoutAnActiveAffiliationNothingIsCreated()
        {
            var harness = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic));
            harness.Staff.Add(harness.User.UserId!.Value, true);

            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Emergencies.OpenAsync(Request(), None));
            Assert.Empty(harness.Store.Encounters);
        }

        [Fact]
        public async Task InactiveStaffCannotOpenAnEmergency()
        {
            var harness = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic));
            harness.Staff.Add(harness.User.UserId!.Value, false, Guid.NewGuid());

            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Emergencies.OpenAsync(Request(), None));
            Assert.Empty(harness.Store.Encounters);
        }

        [Fact]
        public async Task OnlyTheAssignedParamedicReadsTheEmergency()
        {
            var store = new FakeClinicalStore();
            var assigned = Paramedic(store);
            var other = Paramedic(store);
            var opened = await assigned.Emergencies.OpenAsync(Request(), None);

            Assert.Equal(opened.Emergency, await assigned.Emergencies.GetAsync(opened.Emergency.Id, None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => other.Emergencies.GetAsync(opened.Emergency.Id, None));
        }

        [Fact]
        public async Task AnEndedAssignmentNoLongerGivesAccess()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var opened = await harness.Emergencies.OpenAsync(Request(), None);

            harness.Store.Assignments.Single().Unassign(ClinicalHarness.Start.AddMinutes(1));

            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Emergencies.GetAsync(opened.Emergency.Id, None));
        }

        [Fact]
        public async Task OnlyPrehospitalEncountersAreEmergencies()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var hospital = harness.SeedEncounter();

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Emergencies.GetAsync(hospital.Id, None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Emergencies.GetAsync(Guid.NewGuid(), None));
        }

        // The paramedic's base clinic is not where the emergency is: it does not show it, and does not grant access.
        [Fact]
        public async Task AClinicLessEmergencyIsNotPartOfTheBaseClinic()
        {
            var store = new FakeClinicalStore();
            var paramedic = Paramedic(store);
            var opened = await paramedic.Emergencies.OpenAsync(Request(), None);
            var colleague = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic), store);
            colleague.Staff.Add(colleague.User.UserId!.Value, true, paramedic.ClinicId);

            Assert.DoesNotContain(await paramedic.Encounters.ListOpenAsync(paramedic.ClinicId, None), e => e.Id == opened.Emergency.Id);
            await Assert.ThrowsAsync<AccessDeniedException>(() => colleague.Emergencies.GetAsync(opened.Emergency.Id, None));
            // The hospital paths keep their clinic rule: no clinic, no access.
            await Assert.ThrowsAsync<AccessDeniedException>(() => paramedic.Encounters.GetAsync(opened.Emergency.Id, None));
        }

        [Fact]
        public async Task AnAssignmentNeverOpensAClinicLessEncounterOfAnotherType()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var actor = await harness.Actor.GetAsync(None);
            var encounterId = Guid.NewGuid();
            harness.Store.Assignments.Add(EncounterStaffAssignment.Assign(encounterId, actor.StaffId, EncounterStaffFunction.Paramedic, ClinicalHarness.Start));
            var access = new EncounterAccessService(harness.Store);

            await access.EnsureCanAccessAsync(actor, new EncounterAccessTarget(encounterId, ClinicalEncounterType.Prehospital, null), None);
            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                access.EnsureCanAccessAsync(actor, new EncounterAccessTarget(encounterId, ClinicalEncounterType.Emergency, null), None));
        }

        // Once it has a clinic (received at the hospital), the clinic rule applies, as for any encounter.
        [Fact]
        public async Task WithAClinicTheClinicRuleApplies()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var actor = await harness.Actor.GetAsync(None);
            var access = new EncounterAccessService(harness.Store);

            await access.EnsureCanAccessAsync(actor, new EncounterAccessTarget(Guid.NewGuid(), ClinicalEncounterType.Prehospital, harness.ClinicId), None);
            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                access.EnsureCanAccessAsync(actor, new EncounterAccessTarget(Guid.NewGuid(), ClinicalEncounterType.Prehospital, Guid.NewGuid()), None));
        }

        [Fact]
        public void TheRequestHashIsDeterministicAndNormalized()
        {
            var instant = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.FromHours(-4));

            var hash = IdempotencyRequestHash.Compute(IdempotencyRequestHash.Instant(instant), IdempotencyRequestHash.Text("Dolor"));

            Assert.Equal(hash, IdempotencyRequestHash.Compute(
                IdempotencyRequestHash.Instant(instant.ToUniversalTime()), IdempotencyRequestHash.Text("  Dolor ")));
            Assert.NotEqual(hash, IdempotencyRequestHash.Compute(IdempotencyRequestHash.Instant(instant), IdempotencyRequestHash.Text("dolor")));
            // Values cannot be shifted from one field to another.
            Assert.NotEqual(IdempotencyRequestHash.Compute("a", "bc"), IdempotencyRequestHash.Compute("ab", "c"));
            Assert.Equal(IdempotencyRequestHash.Length, hash.Length);
        }

        private static ClinicalHarness Paramedic(FakeClinicalStore store)
        {
            var harness = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic), store);
            harness.Staff.Add(harness.User.UserId!.Value, true, Guid.NewGuid());
            return harness;
        }
    }
}
