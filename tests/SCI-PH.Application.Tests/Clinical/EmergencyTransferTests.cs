using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Entities.Transport;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;
using SCI_PH.Domain.Enums.Transport;

namespace SCI_PH.Application.Tests.Clinical
{
    // Transfer and handoff of a prehospital emergency (PrehospitalEmergencyService): the paramedic requests a transfer to
    // an active clinic (no origin), starts it (InTransit, encounter Transferred) or cancels it, scoped to one transfer;
    // documents on the way (Transferred without clinic); the destination receives it through the Web TransferService
    // (Received, clinic assigned) and from then on no mobile write is accepted.
    public class EmergencyTransferTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private static readonly DateTimeOffset StartedAt = ClinicalHarness.Start.AddMinutes(-10);

        private static async Task<(ClinicalHarness Paramedic, ClinicalEncounter Emergency, Guid Destination)> OpenedEmergencyAsync(
            ClinicStatus destinationStatus = ClinicStatus.Active)
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var opened = await harness.Emergencies.OpenAsync(new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), StartedAt, "Politrauma"), None);
            var destination = SeedDestination(harness, "Hospital Central", destinationStatus);
            return (harness, harness.Store.Encounters.Single(e => e.Id == opened.Emergency.Id), destination);
        }

        private static Guid SeedDestination(
            ClinicalHarness harness, string name, ClinicStatus status = ClinicStatus.Active, ClinicType type = ClinicType.Hospital,
            string municipality = "Santo Domingo Este")
        {
            var id = Guid.NewGuid();
            harness.Store.Clinics[id] = status;
            harness.Store.ClinicDetails[id] = (name, type, municipality, "Santo Domingo");
            return id;
        }

        private static RequestEmergencyTransferRequest Request(Guid destination, Guid? key = null, string? reason = "Trauma mayor", string? notes = null) =>
            new(key ?? Guid.NewGuid(), destination, reason, notes);

        // Staff of the destination clinic acting through the Web TransferService, over the same store.
        private static ClinicalHarness Hospital(ClinicalHarness paramedic, Guid destination, string role = SecurityRoles.Doctor)
        {
            var hospital = new ClinicalHarness(FakeCurrentUser.InRole(role), paramedic.Store);
            hospital.Staff.Add(hospital.User.UserId!.Value, true, destination);
            return hospital;
        }

        private static async Task<EmergencyTransferSummary> InTransitAsync(ClinicalHarness harness, ClinicalEncounter emergency, Guid destination)
        {
            var requested = await harness.Emergencies.RequestTransferAsync(emergency.Id, Request(destination), None);
            return await harness.Emergencies.StartTransferAsync(emergency.Id, requested.Transfer.Id, None);
        }

        private static ClinicalTransfer Transfer(ClinicalHarness harness, Guid id) => harness.Store.Transfers.Single(t => t.Id == id);

        private static int TransferAudits(ClinicalHarness harness, AuditActionType action) =>
            harness.Store.Audit.Count(a => a.EntityName == nameof(ClinicalTransfer) && a.Action == action);

        [Fact]
        public async Task TheDestinationsAreTheActiveClinicsInOrderAndNeedClinicalStaff()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var b = SeedDestination(harness, "Hospital B", type: ClinicType.EmergencyCenter);
            var a = SeedDestination(harness, "Hospital A", type: ClinicType.Clinic, municipality: "Santiago");
            SeedDestination(harness, "Cerrado", ClinicStatus.Inactive);

            var destinations = await harness.Emergencies.ListTransferDestinationsAsync(None);

            Assert.Equal([a, b], destinations.Select(d => d.Id));
            Assert.Equal(new TransferDestinationSummary(a, "Hospital A", ClinicType.Clinic, "Santiago", "Santo Domingo"), destinations[0]);
            var noStaff = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic), harness.Store);
            await Assert.ThrowsAsync<AccessDeniedException>(() => noStaff.Emergencies.ListTransferDestinationsAsync(None));
        }

        // The request on a Created emergency: a Requested transfer without origin, the start of the care, its audit and
        // the receipt, all with the server's time.
        [Fact]
        public async Task ARequestCreatesARequestedTransferWithoutOriginAndStartsTheCare()
        {
            var (harness, emergency, destination) = await OpenedEmergencyAsync();
            Assert.Equal(ClinicalEncounterStatus.Created, emergency.Status);
            var request = Request(destination, notes: "  Sospecha de TCE  ");

            var result = await harness.Emergencies.RequestTransferAsync(emergency.Id, request, None);

            Assert.True(result.Created);
            var transfer = Assert.Single(harness.Store.Transfers);
            Assert.Equal((emergency.Id, (Guid?)null, destination, ClinicalTransferStatus.Requested, ClinicalHarness.Start, "Trauma mayor", "Sospecha de TCE"),
                (transfer.ClinicalEncounterId, transfer.OriginClinicId, transfer.DestinationClinicId, transfer.Status, transfer.RequestedAtUtc, transfer.Reason, transfer.Notes));
            Assert.Equal((ClinicalEncounterStatus.InProgress, (Guid?)null), (emergency.Status, emergency.ClinicId));
            Assert.Equal(new EmergencyTransferSummary(transfer.Id, emergency.Id, ClinicalTransferStatus.Requested,
                new TransferDestinationSummary(destination, "Hospital Central", ClinicType.Hospital, "Santo Domingo Este", "Santo Domingo"),
                ClinicalHarness.Start, null, null), result.Transfer);

            var audit = Assert.Single(harness.Store.Audit, a => a.EntityName == nameof(ClinicalTransfer));
            Assert.Equal((AuditActionType.Create, transfer.Id.ToString(), emergency.Id, (Guid?)null),
                (audit.Action, audit.EntityId, audit.ClinicalEncounterId, audit.PatientId));
            var receipt = harness.Store.Receipts.Single(r => r.Key.Operation == PrehospitalEmergencyService.RequestTransferOperation);
            Assert.Equal((new IdempotencyKey(harness.User.UserId!.Value, PrehospitalEmergencyService.RequestTransferOperation, request.ClientRequestId), transfer.Id),
                (receipt.Key, receipt.ResourceId));
        }

        [Fact]
        public async Task AnInProgressEmergencyStaysInProgress()
        {
            var (harness, emergency, destination) = await OpenedEmergencyAsync();
            emergency.ChangeStatus(ClinicalEncounterStatus.InProgress, ClinicalHarness.Start);
            var updatedAt = emergency.UpdatedAtUtc;

            await harness.Emergencies.RequestTransferAsync(emergency.Id, Request(destination), None);

            Assert.Equal((ClinicalEncounterStatus.InProgress, updatedAt), (emergency.Status, emergency.UpdatedAtUtc));
        }

        [Fact]
        public async Task TheDestinationMustExistAndBeActive()
        {
            var (harness, emergency, inactive) = await OpenedEmergencyAsync(ClinicStatus.Inactive);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Emergencies.RequestTransferAsync(emergency.Id, Request(Guid.NewGuid()), None));
            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.RequestTransferAsync(emergency.Id, Request(inactive), None));

            Assert.Empty(harness.Store.Transfers);
            Assert.Equal(ClinicalEncounterStatus.Created, emergency.Status);
        }

        [Fact]
        public async Task InvalidRequestsAreRejectedOnTheirField()
        {
            var (harness, emergency, destination) = await OpenedEmergencyAsync();

            foreach (var (request, field) in new[]
            {
                (Request(destination, reason: new string('r', 501)), "reason"),
                (Request(destination, notes: new string('n', 4001)), "notes"),
                (Request(Guid.Empty), "destinationClinicId"),
                (Request(destination, Guid.Empty), "clientRequestId")
            })
            {
                var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Emergencies.RequestTransferAsync(emergency.Id, request, None));
                Assert.Equal(field, error.Field);
            }

            await harness.Emergencies.RequestTransferAsync(emergency.Id, Request(destination, reason: new string('r', 500), notes: new string('n', 4000)), None);
            Assert.Single(harness.Store.Transfers);
        }

        // One active transfer per emergency, and none once it is on its way.
        [Fact]
        public async Task AnotherActiveTransferOrAnEmergencyOnItsWayIsAConflict()
        {
            var (harness, emergency, destination) = await OpenedEmergencyAsync();
            var other = SeedDestination(harness, "Otro");
            var first = await harness.Emergencies.RequestTransferAsync(emergency.Id, Request(destination), None);

            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.RequestTransferAsync(emergency.Id, Request(other), None));
            await harness.Emergencies.StartTransferAsync(emergency.Id, first.Transfer.Id, None);
            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.RequestTransferAsync(emergency.Id, Request(other), None));

            Assert.Single(harness.Store.Transfers);
        }

        [Fact]
        public async Task RetryingTheRequestReplaysTheSameTransferAndAnotherPayloadIsAConflict()
        {
            var (harness, emergency, destination) = await OpenedEmergencyAsync();
            var other = SeedDestination(harness, "Otro");
            var key = Guid.NewGuid();
            var first = await harness.Emergencies.RequestTransferAsync(emergency.Id, Request(destination, key), None);
            harness.Time.Now = ClinicalHarness.Start.AddMinutes(30);

            var replay = await harness.Emergencies.RequestTransferAsync(emergency.Id, Request(destination, key, reason: " Trauma mayor "), None);

            Assert.False(replay.Created);
            Assert.Equal(first.Transfer, replay.Transfer);
            foreach (var request in new[]
            {
                Request(other, key),
                Request(destination, key, reason: "Otro motivo"),
                Request(destination, key, notes: "Notas")
            })
            {
                await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.RequestTransferAsync(emergency.Id, request, None));
            }

            Assert.Single(harness.Store.Transfers);
            Assert.Equal(1, TransferAudits(harness, AuditActionType.Create));
        }

        // Start: Requested -> InTransit by the actor at the server's time, the encounter Transferred, one Transfer audit; a
        // second start is a replay without new writes.
        [Fact]
        public async Task StartingTheTransferPutsItInTransitAndTheEmergencyTransferredOnce()
        {
            var (harness, emergency, destination) = await OpenedEmergencyAsync();
            var requested = await harness.Emergencies.RequestTransferAsync(emergency.Id, Request(destination), None);
            harness.Time.Now = ClinicalHarness.Start.AddMinutes(2);

            var started = await harness.Emergencies.StartTransferAsync(emergency.Id, requested.Transfer.Id, None);

            var transfer = Transfer(harness, requested.Transfer.Id);
            var staffId = (await harness.Actor.GetAsync(None)).StaffId;
            Assert.Equal((ClinicalTransferStatus.InTransit, (Guid?)staffId, (DateTimeOffset?)ClinicalHarness.Start.AddMinutes(2)),
                (transfer.Status, transfer.SentByStaffId, transfer.SentAtUtc));
            Assert.Equal((ClinicalEncounterStatus.Transferred, (Guid?)null), (emergency.Status, emergency.ClinicId));
            Assert.Equal((ClinicalTransferStatus.InTransit, (DateTimeOffset?)ClinicalHarness.Start.AddMinutes(2)), (started.Status, started.SentAtUtc));
            var audit = Assert.Single(harness.Store.Audit, a => a.EntityName == nameof(ClinicalTransfer) && a.Action == AuditActionType.Transfer);
            Assert.Contains("InTransit", audit.ChangesJson);
            Assert.Contains("Transferred", audit.ChangesJson);
            var saves = harness.Store.SaveCount;

            var replay = await harness.Emergencies.StartTransferAsync(emergency.Id, requested.Transfer.Id, None);

            Assert.Equal(started, replay);
            Assert.Equal(saves, harness.Store.SaveCount);
            Assert.Equal(1, TransferAudits(harness, AuditActionType.Transfer));
        }

        [Fact]
        public async Task StartAndCancelAreScopedToATransferOfThisEmergency()
        {
            var (harness, emergency, destination) = await OpenedEmergencyAsync();
            var otherEmergency = await harness.Emergencies.OpenAsync(new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), StartedAt, "Caída"), None);
            var foreign = await harness.Emergencies.RequestTransferAsync(otherEmergency.Emergency.Id, Request(destination), None);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Emergencies.StartTransferAsync(emergency.Id, foreign.Transfer.Id, None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Emergencies.CancelTransferAsync(emergency.Id, foreign.Transfer.Id, None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Emergencies.StartTransferAsync(emergency.Id, Guid.NewGuid(), None));

            Assert.Equal(ClinicalTransferStatus.Requested, Transfer(harness, foreign.Transfer.Id).Status);
        }

        [Fact]
        public async Task CancellingARequestedTransferLeavesTheEmergencyInProgressAndIsReplayable()
        {
            var (harness, emergency, destination) = await OpenedEmergencyAsync();
            var requested = await harness.Emergencies.RequestTransferAsync(emergency.Id, Request(destination), None);

            var cancelled = await harness.Emergencies.CancelTransferAsync(emergency.Id, requested.Transfer.Id, None);
            var replay = await harness.Emergencies.CancelTransferAsync(emergency.Id, requested.Transfer.Id, None);

            Assert.Equal((ClinicalTransferStatus.Cancelled, (DateTimeOffset?)ClinicalHarness.Start), (cancelled.Status, cancelled.CancelledAtUtc));
            Assert.Equal(cancelled, replay);
            Assert.Equal(ClinicalEncounterStatus.InProgress, emergency.Status);
            Assert.Equal(1, TransferAudits(harness, AuditActionType.Transfer));
            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Emergencies.StartTransferAsync(emergency.Id, requested.Transfer.Id, None));
        }

        [Fact]
        public async Task CancellingAnInTransitTransferReturnsTheEmergencyToInProgress()
        {
            var (harness, emergency, destination) = await OpenedEmergencyAsync();
            var started = await InTransitAsync(harness, emergency, destination);

            var cancelled = await harness.Emergencies.CancelTransferAsync(emergency.Id, started.Id, None);

            Assert.Equal(ClinicalTransferStatus.Cancelled, cancelled.Status);
            Assert.Equal(ClinicalEncounterStatus.InProgress, emergency.Status);
            var audit = harness.Store.Audit.Last(a => a.EntityName == nameof(ClinicalTransfer));
            Assert.Contains("Cancelled", audit.ChangesJson);
            Assert.Contains("InProgress", audit.ChangesJson);
        }

        // A wrong destination is corrected with cancel + a new request; a late retry of the first cancel never touches the
        // second transfer.
        [Fact]
        public async Task ALateCancelOfAnOlderTransferDoesNotCancelTheNewOne()
        {
            var (harness, emergency, destination) = await OpenedEmergencyAsync();
            var wrong = await harness.Emergencies.RequestTransferAsync(emergency.Id, Request(SeedDestination(harness, "Equivocado")), None);
            await harness.Emergencies.CancelTransferAsync(emergency.Id, wrong.Transfer.Id, None);
            var right = await harness.Emergencies.RequestTransferAsync(emergency.Id, Request(destination), None);

            var lateRetry = await harness.Emergencies.CancelTransferAsync(emergency.Id, wrong.Transfer.Id, None);

            Assert.Equal(ClinicalTransferStatus.Cancelled, lateRetry.Status);
            Assert.Equal(ClinicalTransferStatus.Requested, Transfer(harness, right.Transfer.Id).Status);
        }

        // On the way: the four records are accepted on the Transferred emergency without clinic.
        [Fact]
        public async Task ThePrehospitalRecordsAreAcceptedOnTheWay()
        {
            var (harness, emergency, destination) = await OpenedEmergencyAsync();
            var medication = harness.SeedMedication();
            await InTransitAsync(harness, emergency, destination);
            Assert.Equal(ClinicalEncounterStatus.Transferred, emergency.Status);

            await harness.Emergencies.RecordVitalSignsAsync(emergency.Id,
                new RecordEmergencyVitalSignsRequest(Guid.NewGuid(), ClinicalHarness.Start, HeartRate: 90), None);
            await harness.Emergencies.RecordProcedureAsync(emergency.Id,
                new RecordEmergencyProcedureRequest(Guid.NewGuid(), "OXYGEN-THERAPY", ClinicalHarness.Start), None);
            await harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id,
                new RecordEmergencyMedicationAdministrationRequest(Guid.NewGuid(), medication.Id, 1m, "g", MedicationRoute.Intravenous, ClinicalHarness.Start), None);
            await harness.Emergencies.RecordNoteAsync(emergency.Id,
                new RecordEmergencyNoteRequest(Guid.NewGuid(), ClinicalHarness.Start, "Estable durante el traslado."), None);

            Assert.Equal((1, 1, 1, 1), (harness.Store.VitalSigns.Count, harness.Store.Procedures.Count, harness.Store.Administrations.Count, harness.Store.Notes.Count));
            Assert.Equal(ClinicalEncounterStatus.Transferred, emergency.Status);
        }

        [Fact]
        public async Task ThePatientCanBeIdentifiedByDocumentOnTheWay()
        {
            var (harness, emergency, destination) = await OpenedEmergencyAsync();
            await InTransitAsync(harness, emergency, destination);
            var patient = Patient.Register("Ana", "Pérez", new DateOnly(1980, 1, 1), BiologicalSex.Female);
            patient.AddIdentifier(PatientIdentifierType.NationalId, "001-1234567-8", PatientIdentifierSource.Manual, isPrimary: true);
            harness.Store.Patients.Add(patient);

            await harness.Documents.ConfirmAsync(emergency.Id,
                new IdentifyEmergencyByDocumentRequest(Guid.NewGuid(), PatientIdentifierType.NationalId, "001-1234567-8"), None);
            var cancelledLater = await harness.Emergencies.CancelTransferAsync(emergency.Id, harness.Store.Transfers.Single().Id, None);

            Assert.Equal((patient.Id, ClinicalEncounterStatus.InProgress), (emergency.PatientId, emergency.Status));
            Assert.Equal(patient.Id, harness.Store.Audit.Last(a => a.EntityName == nameof(ClinicalTransfer)).PatientId);
            Assert.Equal(ClinicalTransferStatus.Cancelled, cancelledLater.Status);
        }

        // Handoff: the destination receives the transfer through the Web TransferService. The encounter becomes the
        // clinic's; every mobile write of the paramedic is then rejected (409), even when the paramedic is also affiliated
        // with that clinic, and the hospital records care.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task AfterTheHandoffNoMobileWriteIsAcceptedAndTheHospitalTakesOver(bool paramedicAffiliatedWithDestination)
        {
            var (harness, emergency, other) = await OpenedEmergencyAsync();
            var destination = paramedicAffiliatedWithDestination ? harness.ClinicId : other;
            if (paramedicAffiliatedWithDestination)
            {
                harness.Store.Clinics[destination] = ClinicStatus.Active;
            }

            var medication = harness.SeedMedication();
            var started = await InTransitAsync(harness, emergency, destination);
            var hospital = Hospital(harness, destination);

            var received = await hospital.Transfers.ReceiveAsync(started.Id, None);

            Assert.Equal((ClinicalTransferStatus.Received, ClinicalEncounterStatus.Received, (Guid?)destination),
                (received.Status, emergency.Status, emergency.ClinicId));
            foreach (var write in new Func<Task>[]
            {
                () => harness.Emergencies.RecordVitalSignsAsync(emergency.Id, new RecordEmergencyVitalSignsRequest(Guid.NewGuid(), ClinicalHarness.Start, HeartRate: 90), None),
                () => harness.Emergencies.RecordProcedureAsync(emergency.Id, new RecordEmergencyProcedureRequest(Guid.NewGuid(), "CPR", ClinicalHarness.Start), None),
                () => harness.Emergencies.RecordMedicationAdministrationAsync(emergency.Id, new RecordEmergencyMedicationAdministrationRequest(Guid.NewGuid(), medication.Id, 1m, "g", MedicationRoute.Oral, ClinicalHarness.Start), None),
                () => harness.Emergencies.RecordNoteAsync(emergency.Id, new RecordEmergencyNoteRequest(Guid.NewGuid(), ClinicalHarness.Start, "Tarde."), None),
                () => harness.Emergencies.CancelTransferAsync(emergency.Id, started.Id, None),
                () => harness.Emergencies.StartTransferAsync(emergency.Id, started.Id, None),
                () => harness.Emergencies.RequestTransferAsync(emergency.Id, Request(other), None)
            })
            {
                await Assert.ThrowsAsync<ResourceConflictException>(write);
            }

            Assert.Empty(harness.Store.VitalSigns);
            await hospital.Records.RecordVitalSignAsync(emergency.Id, new RecordVitalSignRequest(VitalSignType.HeartRate, 88, ClinicalHarness.Start), None);
            Assert.Single(harness.Store.VitalSigns);
        }

        // Before the handoff the destination sees the incoming transfer but not the encounter itself.
        [Fact]
        public async Task BeforeTheHandoffTheHospitalSeesTheIncomingTransferButNotTheEncounter()
        {
            var (harness, emergency, destination) = await OpenedEmergencyAsync();
            var started = await InTransitAsync(harness, emergency, destination);
            var hospital = Hospital(harness, destination);

            var incoming = Assert.Single(await hospital.Transfers.ListIncomingAsync(null, None));

            Assert.Equal((started.Id, (ClinicSummary?)null, emergency.TemporaryPatientCode!.Value), (incoming.Id, incoming.OriginClinic, incoming.TemporaryPatientCode));
            await Assert.ThrowsAsync<AccessDeniedException>(() => hospital.Records.RecordVitalSignAsync(emergency.Id,
                new RecordVitalSignRequest(VitalSignType.HeartRate, 88, ClinicalHarness.Start), None));
        }

        [Fact]
        public async Task OnlyTheAssignedParamedicManagesTheTransferAndOtherStaffGetA403AfterTheHandoff()
        {
            var (harness, emergency, destination) = await OpenedEmergencyAsync();
            var other = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic), harness.Store);
            other.Staff.Add(other.User.UserId!.Value, true, Guid.NewGuid());

            await Assert.ThrowsAsync<AccessDeniedException>(() => other.Emergencies.RequestTransferAsync(emergency.Id, Request(destination), None));
            var started = await InTransitAsync(harness, emergency, destination);
            await Assert.ThrowsAsync<AccessDeniedException>(() => other.Emergencies.CancelTransferAsync(emergency.Id, started.Id, None));
            await Hospital(harness, destination).Transfers.ReceiveAsync(started.Id, None);
            await Assert.ThrowsAsync<AccessDeniedException>(() => other.Emergencies.RecordNoteAsync(emergency.Id,
                new RecordEmergencyNoteRequest(Guid.NewGuid(), ClinicalHarness.Start, "x"), None));
        }

        [Fact]
        public async Task ANonPrehospitalEncounterIsNotAnEmergency()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var hospital = harness.SeedEncounter();

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Emergencies.RequestTransferAsync(hospital.Id, Request(SeedDestination(harness, "X")), None));
        }

        [Fact]
        public async Task AReceivedTransferCannotBeCancelledByTheWeb()
        {
            var (harness, emergency, destination) = await OpenedEmergencyAsync();
            var started = await InTransitAsync(harness, emergency, destination);
            var hospital = Hospital(harness, destination);
            await hospital.Transfers.ReceiveAsync(started.Id, None);

            await Assert.ThrowsAsync<AccessDeniedException>(() => hospital.Transfers.CancelAsync(started.Id, None));
            Assert.Equal(ClinicalTransferStatus.Received, Transfer(harness, started.Id).Status);
        }

        // The emergency carries its active transfer (Requested or InTransit) so the app can restore its transfer screen.
        [Fact]
        public async Task TheEmergencyShowsItsActiveTransfer()
        {
            var (harness, emergency, destination) = await OpenedEmergencyAsync();
            Assert.Null((await harness.Emergencies.GetAsync(emergency.Id, None)).ActiveTransfer);

            var requested = await harness.Emergencies.RequestTransferAsync(emergency.Id, Request(destination), None);
            Assert.Equal(requested.Transfer, (await harness.Emergencies.GetAsync(emergency.Id, None)).ActiveTransfer);

            var started = await harness.Emergencies.StartTransferAsync(emergency.Id, requested.Transfer.Id, None);
            Assert.Equal(started, (await harness.Emergencies.GetAsync(emergency.Id, None)).ActiveTransfer);

            await harness.Emergencies.CancelTransferAsync(emergency.Id, started.Id, None);
            Assert.Null((await harness.Emergencies.GetAsync(emergency.Id, None)).ActiveTransfer);
        }

        // Regression: the Web transfer steps still need an origin clinic, so they cannot dispatch or cancel a prehospital
        // transfer; the Web records keep refusing a Transferred encounter.
        [Fact]
        public async Task TheWebStepsKeepTheirClinicRules()
        {
            var (harness, emergency, destination) = await OpenedEmergencyAsync();
            var requested = await harness.Emergencies.RequestTransferAsync(emergency.Id, Request(destination), None);
            var hospital = Hospital(harness, destination);

            await Assert.ThrowsAsync<AccessDeniedException>(() => hospital.Transfers.DispatchAsync(requested.Transfer.Id, None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => hospital.Transfers.CancelAsync(requested.Transfer.Id, None));
            Assert.DoesNotContain(ClinicalEncounterStatus.Transferred, EncounterRecordService.RecordableStatuses);
        }
    }
}
