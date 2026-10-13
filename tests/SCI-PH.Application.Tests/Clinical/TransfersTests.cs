using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Transport;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Transport;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Application.Tests.Clinical
{
    public class TransfersTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        // Clinic A (origin, the harness user) and clinic B (destination, another user sharing the same store).
        private sealed record Setup(ClinicalHarness Origin, ClinicalHarness Destination, ClinicalEncounter Encounter, Guid ClinicA, Guid ClinicB);

        private static Setup InProgress(string originRole = SecurityRoles.Doctor, string destinationRole = SecurityRoles.Doctor)
        {
            var origin = ClinicalHarness.ForRole(originRole);
            var clinicA = origin.ClinicId;
            var clinicB = Guid.NewGuid();
            origin.Store.Clinics[clinicA] = ClinicStatus.Active;
            origin.Store.Clinics[clinicB] = ClinicStatus.Active;
            var encounter = origin.SeedEncounter(patientId: origin.SeedPatient().Id);
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, ClinicalHarness.Start);
            origin.Time.Now = ClinicalHarness.Start.AddMinutes(30);
            return new Setup(origin, Other(origin, clinicB, destinationRole), encounter, clinicA, clinicB);
        }

        private static ClinicalHarness Other(ClinicalHarness origin, Guid clinicId, string role = SecurityRoles.Doctor)
        {
            var other = new ClinicalHarness(FakeCurrentUser.InRole(role), origin.Store);
            other.Staff.Add(other.User.UserId!.Value, true, clinicId);
            other.Time.Now = origin.Time.Now;
            return other;
        }

        private static async Task<ClinicalTransferSummary> InTransitAsync(Setup s)
        {
            var requested = await s.Origin.Transfers.RequestAsync(s.Encounter.Id, new RequestTransferRequest(s.ClinicB, "Requiere UCI"), None);
            s.Origin.Time.Now = s.Origin.Time.Now.AddMinutes(5);
            s.Destination.Time.Now = s.Origin.Time.Now.AddMinutes(30);
            return await s.Origin.Transfers.DispatchAsync(requested.Id, None);
        }

        [Fact]
        public async Task RequestRecordsOriginAndDestinationButKeepsTheEncounterStatus()
        {
            var s = InProgress();

            var transfer = await s.Origin.Transfers.RequestAsync(s.Encounter.Id, new RequestTransferRequest(s.ClinicB, "Requiere UCI"), None);

            Assert.Equal(ClinicalTransferStatus.Requested, transfer.Status);
            Assert.Equal(s.ClinicA, transfer.OriginClinic!.Id);
            Assert.Equal(s.ClinicB, transfer.DestinationClinic.Id);
            Assert.Equal(ClinicalEncounterStatus.InProgress, s.Encounter.Status);
            Assert.Equal(ClinicalEncounterStatus.InProgress, transfer.EncounterStatus);
            Assert.Equal(s.ClinicA, s.Encounter.ClinicId);
            Assert.Contains(s.Encounter.Id, s.Origin.Store.GuardedEncounters);
            Assert.Equal(AuditActionType.Create, Assert.Single(s.Origin.Store.Audit).Action);
        }

        [Fact]
        public async Task RequestValidatesTheDestinationAndAllowsOneActiveTransfer()
        {
            var s = InProgress();
            var inactive = Guid.NewGuid();
            s.Origin.Store.Clinics[inactive] = ClinicStatus.Inactive;
            var transfers = s.Origin.Transfers;

            var empty = await Assert.ThrowsAsync<RequestValidationException>(() =>
                transfers.RequestAsync(s.Encounter.Id, new RequestTransferRequest(Guid.Empty), None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => transfers.RequestAsync(s.Encounter.Id, new RequestTransferRequest(Guid.NewGuid()), None));
            await Assert.ThrowsAsync<ResourceConflictException>(() => transfers.RequestAsync(s.Encounter.Id, new RequestTransferRequest(inactive), None));
            // Same origin and destination is a Domain rule (400).
            await Assert.ThrowsAsync<InvalidClinicalTransferException>(() => transfers.RequestAsync(s.Encounter.Id, new RequestTransferRequest(s.ClinicA), None));
            await transfers.RequestAsync(s.Encounter.Id, new RequestTransferRequest(s.ClinicB), None);
            await Assert.ThrowsAsync<ResourceConflictException>(() => transfers.RequestAsync(s.Encounter.Id, new RequestTransferRequest(s.ClinicB), None));

            Assert.Equal("destinationClinicId", empty.Field);
            Assert.Single(s.Origin.Store.Transfers);
        }

        [Fact]
        public async Task OnlyAnOpenEncounterOfTheActorsClinicCanBeTransferred()
        {
            var s = InProgress();
            var created = s.Origin.SeedEncounter(patientId: s.Origin.SeedPatient().Id);
            var foreign = s.Origin.SeedEncounter(clinicId: s.ClinicB, patientId: s.Origin.SeedPatient().Id);
            foreign.ChangeStatus(ClinicalEncounterStatus.InProgress, ClinicalHarness.Start);
            var request = new RequestTransferRequest(s.ClinicB);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => s.Origin.Transfers.RequestAsync(Guid.NewGuid(), request, None));
            await Assert.ThrowsAsync<ResourceConflictException>(() => s.Origin.Transfers.RequestAsync(created.Id, request, None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Origin.Transfers.RequestAsync(foreign.Id, request, None));
            await s.Origin.Encounters.CloseAsync(s.Encounter.Id, new CloseEncounterRequest(), None);
            await Assert.ThrowsAsync<ResourceConflictException>(() => s.Origin.Transfers.RequestAsync(s.Encounter.Id, request, None));
        }

        [Fact]
        public async Task WithoutTransfersManageNoTransferIsWritten()
        {
            var s = InProgress(originRole: SecurityRoles.Nurse);

            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                s.Origin.Transfers.RequestAsync(s.Encounter.Id, new RequestTransferRequest(s.ClinicB), None));
            Assert.Empty(s.Origin.Store.Transfers);
        }

        [Fact]
        public async Task DispatchSendsThePatientFromTheOriginClinic()
        {
            var s = InProgress();

            var transfer = await InTransitAsync(s);

            Assert.Equal(ClinicalTransferStatus.InTransit, transfer.Status);
            Assert.Equal((await s.Origin.Actor.GetAsync(None)).StaffId, transfer.SentByStaffId);
            Assert.Equal(ClinicalEncounterStatus.Transferred, s.Encounter.Status);
            // While in transit the encounter still belongs to the origin clinic.
            Assert.Equal(s.ClinicA, s.Encounter.ClinicId);
            Assert.Contains(s.Origin.Store.Audit, a => a.Action == AuditActionType.Transfer);
            await Assert.ThrowsAsync<ResourceConflictException>(() => s.Origin.Transfers.DispatchAsync(transfer.Id, None));
        }

        [Fact]
        public async Task OnlyTheOriginClinicDispatchesAndAClosedEncounterCannotLeave()
        {
            var s = InProgress();
            var requested = await s.Origin.Transfers.RequestAsync(s.Encounter.Id, new RequestTransferRequest(s.ClinicB), None);

            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Destination.Transfers.DispatchAsync(requested.Id, None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => s.Origin.Transfers.DispatchAsync(Guid.NewGuid(), None));
            // The encounter changed while the transfer was only requested.
            await s.Origin.Encounters.CloseAsync(s.Encounter.Id, new CloseEncounterRequest(), None);
            await Assert.ThrowsAsync<ResourceConflictException>(() => s.Origin.Transfers.DispatchAsync(requested.Id, None));
            Assert.Equal(ClinicalTransferStatus.Requested, s.Origin.Store.Transfers.Single().Status);
        }

        [Fact]
        public async Task IncomingListsOnlyTransfersToTheActorsClinics()
        {
            var s = InProgress();
            var transfer = await InTransitAsync(s);

            var incoming = await s.Destination.Transfers.ListIncomingAsync(null, None);
            var atOrigin = await s.Origin.Transfers.ListIncomingAsync(null, None);

            Assert.Equal(transfer.Id, Assert.Single(incoming).Id);
            Assert.Equal("Ana Pérez", incoming[0].Patient!.DisplayName);
            Assert.StartsWith("***", incoming[0].Patient!.PrimaryIdentifier!.Value);
            Assert.Empty(atOrigin);
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Destination.Transfers.ListIncomingAsync(s.ClinicA, None));
            Assert.Single(await s.Destination.Transfers.ListIncomingAsync(s.ClinicB, None));
        }

        [Fact]
        public async Task ReceptionMovesTheSameEncounterToTheDestinationClinic()
        {
            var s = InProgress();
            var transfer = await InTransitAsync(s);
            var receiver = await s.Destination.Actor.GetAsync(None);

            var received = await s.Destination.Transfers.ReceiveAsync(transfer.Id, None);

            Assert.Equal(ClinicalTransferStatus.Received, received.Status);
            Assert.Equal(receiver.StaffId, received.ReceivedByStaffId);
            Assert.Equal(ClinicalEncounterStatus.Received, received.EncounterStatus);
            Assert.Equal(s.ClinicB, s.Encounter.ClinicId);
            Assert.Empty(await s.Destination.Transfers.ListIncomingAsync(null, None));
            // Destination continues the same encounter; the origin loses normal clinical access.
            Assert.Equal(ClinicalEncounterStatus.Received, (await s.Destination.Encounters.GetAsync(s.Encounter.Id, None)).Status);
            await s.Destination.Records.RecordNoteAsync(s.Encounter.Id,
                new RecordClinicalNoteRequest(ClinicalNoteType.Evolution, "Llega estable (dato de prueba).", s.Destination.Time.Now), None);
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Origin.Encounters.GetAsync(s.Encounter.Id, None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Origin.Records.ListNotesAsync(s.Encounter.Id, None));
            await s.Destination.Encounters.CloseAsync(s.Encounter.Id, new CloseEncounterRequest(), None);
            Assert.Equal(ClinicalEncounterStatus.Closed, s.Encounter.Status);
            // The origin keeps the trace of the transfer.
            Assert.Equal(s.ClinicA, s.Origin.Store.Transfers.Single().OriginClinicId);
        }

        [Fact]
        public async Task OnlyTheDestinationReceivesOnceAnInTransitTransfer()
        {
            var s = InProgress();
            var requested = await s.Origin.Transfers.RequestAsync(s.Encounter.Id, new RequestTransferRequest(s.ClinicB), None);
            var third = Other(s.Origin, Guid.NewGuid());

            await Assert.ThrowsAsync<ResourceConflictException>(() => s.Destination.Transfers.ReceiveAsync(requested.Id, None));
            s.Origin.Time.Now = s.Origin.Time.Now.AddMinutes(1);
            await s.Origin.Transfers.DispatchAsync(requested.Id, None);
            s.Destination.Time.Now = s.Origin.Time.Now.AddMinutes(20);

            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Origin.Transfers.ReceiveAsync(requested.Id, None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => third.Transfers.ReceiveAsync(requested.Id, None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => third.Transfers.GetAsync(requested.Id, None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => s.Destination.Transfers.ReceiveAsync(Guid.NewGuid(), None));
            await s.Destination.Transfers.ReceiveAsync(requested.Id, None);
            await Assert.ThrowsAsync<ResourceConflictException>(() => s.Destination.Transfers.ReceiveAsync(requested.Id, None));
            Assert.Equal(s.ClinicB, s.Encounter.ClinicId);
        }

        [Fact]
        public async Task CancellingARequestedTransferLeavesTheEncounterAndAllowsANewRequest()
        {
            var s = InProgress();
            var requested = await s.Origin.Transfers.RequestAsync(s.Encounter.Id, new RequestTransferRequest(s.ClinicB), None);

            var cancelled = await s.Origin.Transfers.CancelAsync(requested.Id, None);

            Assert.Equal(ClinicalTransferStatus.Cancelled, cancelled.Status);
            Assert.NotNull(cancelled.CancelledAtUtc);
            Assert.Equal(ClinicalEncounterStatus.InProgress, s.Encounter.Status);
            Assert.Equal(AuditActionType.Transfer, s.Origin.Store.Audit.Last().Action);
            var again = await s.Origin.Transfers.RequestAsync(s.Encounter.Id, new RequestTransferRequest(s.ClinicB), None);
            Assert.Equal(ClinicalTransferStatus.Requested, again.Status);
        }

        [Fact]
        public async Task CancellingAnInTransitTransferReturnsTheEncounterToInProgressAtTheOrigin()
        {
            var s = InProgress();
            var transfer = await InTransitAsync(s);
            s.Origin.Time.Now = s.Origin.Time.Now.AddMinutes(10);

            var cancelled = await s.Origin.Transfers.CancelAsync(transfer.Id, None);

            Assert.Equal(ClinicalTransferStatus.Cancelled, cancelled.Status);
            Assert.Equal(ClinicalEncounterStatus.InProgress, s.Encounter.Status);
            Assert.Equal(s.ClinicA, s.Encounter.ClinicId);
            Assert.Empty(await s.Destination.Transfers.ListIncomingAsync(null, None));
            await Assert.ThrowsAsync<ResourceConflictException>(() => s.Destination.Transfers.ReceiveAsync(transfer.Id, None));
            Assert.Equal(ClinicalTransferStatus.Requested,
                (await s.Origin.Transfers.RequestAsync(s.Encounter.Id, new RequestTransferRequest(s.ClinicB), None)).Status);
        }

        [Fact]
        public async Task OnlyTheOriginCancelsAndOnlyAnActiveTransfer()
        {
            var s = InProgress();
            var transfer = await InTransitAsync(s);
            var third = Other(s.Origin, Guid.NewGuid());

            await Assert.ThrowsAsync<AccessDeniedException>(() => s.Destination.Transfers.CancelAsync(transfer.Id, None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => third.Transfers.CancelAsync(transfer.Id, None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => s.Origin.Transfers.CancelAsync(Guid.NewGuid(), None));
            await s.Destination.Transfers.ReceiveAsync(transfer.Id, None);
            // A received transfer cannot be cancelled (and the encounter is no longer the origin's).
            await Assert.ThrowsAsync<ResourceConflictException>(() => s.Origin.Transfers.CancelAsync(transfer.Id, None));
            Assert.Equal(ClinicalTransferStatus.Received, s.Origin.Store.Transfers.Single().Status);

            var other = InProgress();
            var requested = await other.Origin.Transfers.RequestAsync(other.Encounter.Id, new RequestTransferRequest(other.ClinicB), None);
            await other.Origin.Transfers.CancelAsync(requested.Id, None);
            await Assert.ThrowsAsync<ResourceConflictException>(() => other.Origin.Transfers.CancelAsync(requested.Id, None));
        }

        [Fact]
        public async Task WithoutTransfersManageNothingIsCancelled()
        {
            var s = InProgress();
            var transfer = await InTransitAsync(s);
            var nurse = Other(s.Origin, s.ClinicA, SecurityRoles.Nurse);

            await Assert.ThrowsAsync<AccessDeniedException>(() => nurse.Transfers.CancelAsync(transfer.Id, None));
            Assert.Equal(ClinicalTransferStatus.InTransit, s.Origin.Store.Transfers.Single().Status);
        }

        [Fact]
        public async Task InvalidActorsCannotManageTransfers()
        {
            var s = InProgress();
            var transfer = await InTransitAsync(s);
            var noStaff = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Doctor), s.Origin.Store);
            var inactive = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Doctor), s.Origin.Store);
            inactive.Staff.Add(inactive.User.UserId!.Value, false, s.ClinicB);

            await Assert.ThrowsAsync<AccessDeniedException>(() => noStaff.Transfers.ReceiveAsync(transfer.Id, None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => inactive.Transfers.ReceiveAsync(transfer.Id, None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => noStaff.Transfers.ListIncomingAsync(null, None));
            Assert.Equal(ClinicalEncounterStatus.Transferred, s.Encounter.Status);
        }

        [Fact]
        public async Task APrehospitalEncounterArrivesAtTheHospitalAsTheSameEncounter()
        {
            var hospital = ClinicalHarness.ForRole(SecurityRoles.Doctor);
            var encounter = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Prehospital, ClinicalHarness.Start);
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, ClinicalHarness.Start);
            encounter.ChangeStatus(ClinicalEncounterStatus.Transferred, ClinicalHarness.Start.AddMinutes(5));
            var transfer = ClinicalTransfer.Request(encounter.Id, hospital.ClinicId, ClinicalHarness.Start.AddMinutes(2));
            transfer.Dispatch(Guid.NewGuid(), ClinicalHarness.Start.AddMinutes(5));
            hospital.Store.Encounters.Add(encounter);
            hospital.Store.Transfers.Add(transfer);
            hospital.Time.Now = ClinicalHarness.Start.AddMinutes(30);

            var incoming = Assert.Single(await hospital.Transfers.ListIncomingAsync(null, None));
            await hospital.Transfers.ReceiveAsync(transfer.Id, None);

            Assert.Null(incoming.OriginClinic);
            Assert.Equal(hospital.ClinicId, encounter.ClinicId);
            Assert.Equal(ClinicalEncounterStatus.Received, encounter.Status);
            Assert.NotNull(encounter.TemporaryPatientCode);
        }
    }
}
