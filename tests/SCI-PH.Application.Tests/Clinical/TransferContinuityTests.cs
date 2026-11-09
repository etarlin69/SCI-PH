using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Medications;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Transport;
using SCI_PH.Domain.Exceptions;

namespace SCI_PH.Application.Tests.Clinical
{
    // Hospital continuity after a transfer: the first clinical record at the receiving clinic starts care
    // (Received -> InProgress), which is what lets that clinic transfer the patient again. Plus the two reads the
    // outgoing-transfer workspace needs: destinations and the encounter's transfers.
    public class TransferContinuityTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        private static ClinicalHarness Staff(FakeClinicalStore store, Guid clinicId, string role = SecurityRoles.Doctor, DateTimeOffset? now = null)
        {
            var harness = new ClinicalHarness(FakeCurrentUser.InRole(role), store);
            harness.Staff.Add(harness.User.UserId!.Value, true, clinicId);
            harness.Time.Now = now ?? ClinicalHarness.Start.AddMinutes(30);
            return harness;
        }

        // An encounter that arrived at the harness's clinic from a transfer: Received, nothing recorded there yet.
        private static ClinicalEncounter ReceivedAt(ClinicalHarness harness, Guid? patientId = null)
        {
            var encounter = harness.SeedEncounter(clinicId: Guid.NewGuid(), patientId: patientId);
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, ClinicalHarness.Start);
            encounter.ChangeStatus(ClinicalEncounterStatus.Transferred, ClinicalHarness.Start.AddMinutes(5));
            encounter.ReceiveAtClinic(harness.ClinicId, ClinicalHarness.Start.AddMinutes(10));
            harness.Time.Now = ClinicalHarness.Start.AddMinutes(30);
            return encounter;
        }

        private static Task RecordAsync(ClinicalHarness harness, Guid encounterId, string kind, Guid medicationId)
        {
            var now = harness.Time.Now;
            return kind switch
            {
                "triage" => harness.Records.RecordTriageAsync(encounterId, new RecordTriageRequest(TriagePriority.Urgent), None),
                "vital" => harness.Records.RecordVitalSignAsync(encounterId, new RecordVitalSignRequest(VitalSignType.HeartRate, 88, now), None),
                "note" => harness.Records.RecordNoteAsync(
                    encounterId, new RecordClinicalNoteRequest(ClinicalNoteType.Evolution, "Llega estable (dato de prueba).", now), None),
                "diagnosis" => harness.Records.RecordDiagnosisAsync(
                    encounterId, new RecordDiagnosisRequest("Diagnóstico de prueba", DiagnosisType.Presumptive), None),
                "prescription" => harness.Medications.CreatePrescriptionAsync(encounterId, new CreatePrescriptionRequest(
                    [new PrescriptionDetailInput(medicationId, 500, "mg", MedicationRoute.Oral, "cada 8 horas", 5, null)], "Prueba"), None),
                "administration" => harness.Medications.RecordAdministrationAsync(
                    encounterId, new RecordMedicationAdministrationRequest(medicationId, 500, "mg", MedicationRoute.Oral, now), None),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        public static TheoryData<string> HospitalWrites { get; } =
            ["triage", "vital", "note", "diagnosis", "prescription", "administration"];

        [Theory]
        [MemberData(nameof(HospitalWrites))]
        public async Task EveryHospitalClinicalWriteStartsCareOnAReceivedEncounter(string kind)
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Doctor);
            var medication = harness.SeedMedication();
            var encounter = ReceivedAt(harness, patientId: harness.SeedPatient().Id);
            var saves = harness.Store.SaveCount;

            await RecordAsync(harness, encounter.Id, kind, medication.Id);

            Assert.Equal(ClinicalEncounterStatus.InProgress, encounter.Status);
            // The status change and the record are one save (one transaction), with the encounter guarded.
            Assert.Equal(saves + 1, harness.Store.SaveCount);
            Assert.Contains(encounter.Id, harness.Store.GuardedEncounters);
        }

        [Theory]
        [MemberData(nameof(HospitalWrites))]
        public async Task EveryHospitalClinicalWriteStillStartsCareOnACreatedEncounter(string kind)
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Doctor);
            var medication = harness.SeedMedication();
            var encounter = harness.SeedEncounter(patientId: harness.SeedPatient().Id);
            harness.Time.Now = ClinicalHarness.Start.AddMinutes(30);

            await RecordAsync(harness, encounter.Id, kind, medication.Id);

            Assert.Equal(ClinicalEncounterStatus.InProgress, encounter.Status);
        }

        [Fact]
        public async Task ASecondRecordLeavesTheEncounterInProgress()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Doctor);
            var encounter = ReceivedAt(harness);
            await RecordAsync(harness, encounter.Id, "vital", Guid.Empty);
            var changedAt = encounter.UpdatedAtUtc;
            harness.Time.Now = harness.Time.Now.AddMinutes(5);

            await RecordAsync(harness, encounter.Id, "note", Guid.Empty);

            Assert.Equal(ClinicalEncounterStatus.InProgress, encounter.Status);
            Assert.Equal(changedAt, encounter.UpdatedAtUtc);
        }

        [Fact]
        public async Task ARejectedRecordDoesNotStartCare()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Doctor);
            var encounter = ReceivedAt(harness);
            var saves = harness.Store.SaveCount;

            // Out of range: the record is rejected before the status is touched, and nothing is saved.
            await Assert.ThrowsAsync<DomainException>(() => harness.Records.RecordVitalSignAsync(
                encounter.Id, new RecordVitalSignRequest(VitalSignType.HeartRate, 301, harness.Time.Now), None));
            await Assert.ThrowsAsync<RequestValidationException>(() => harness.Records.RecordVitalSignAsync(
                encounter.Id, new RecordVitalSignRequest(VitalSignType.HeartRate, 80, harness.Time.Now.AddHours(1)), None));

            Assert.Equal(ClinicalEncounterStatus.Received, encounter.Status);
            Assert.Equal(saves, harness.Store.SaveCount);
            Assert.Empty(harness.Store.VitalSigns);
        }

        [Fact]
        public async Task OpeningOrReadingAReceivedEncounterDoesNotStartCare()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Doctor);
            var encounter = ReceivedAt(harness);

            await harness.Encounters.GetAsync(encounter.Id, None);
            await harness.Records.ListNotesAsync(encounter.Id, None);
            await harness.Records.ListTriagesAsync(encounter.Id, None);
            await harness.Transfers.ListForEncounterAsync(encounter.Id, None);

            Assert.Equal(ClinicalEncounterStatus.Received, encounter.Status);
        }

        [Fact]
        public async Task AReceivedEncounterCanStillBeDischargedWithoutARecord()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Doctor);
            var encounter = ReceivedAt(harness);

            await harness.Encounters.CloseAsync(encounter.Id, new CloseEncounterRequest(), None);

            Assert.Equal(ClinicalEncounterStatus.Closed, encounter.Status);
        }

        // Origin A (paramedic arrival), then B, then C: the same NN encounter all the way, one active transfer at a time.
        [Fact]
        public async Task TheSameEncounterContinuesThroughTwoHospitals()
        {
            var store = new FakeClinicalStore();
            var (clinicA, clinicB, clinicC) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            store.Clinics[clinicA] = ClinicStatus.Active;
            store.Clinics[clinicB] = ClinicStatus.Active;
            store.Clinics[clinicC] = ClinicStatus.Active;
            var a = Staff(store, clinicA);
            var encounter = ClinicalEncounter.CreateUnidentified(ClinicalEncounterType.Emergency, ClinicalHarness.Start, clinicA);
            store.Encounters.Add(encounter);
            var nnCode = encounter.TemporaryPatientCode!.Value;
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, ClinicalHarness.Start);
            await a.Records.RecordNoteAsync(encounter.Id,
                new RecordClinicalNoteRequest(ClinicalNoteType.Assessment, "Nota en A (dato de prueba).", a.Time.Now), None);

            // A -> B
            var toB = await a.Transfers.RequestAsync(encounter.Id, new RequestTransferRequest(clinicB, "Requiere UCI"), None);
            a.Time.Now = a.Time.Now.AddMinutes(5);
            await a.Transfers.DispatchAsync(toB.Id, None);
            var b = Staff(store, clinicB, now: a.Time.Now.AddMinutes(30));
            await b.Transfers.ReceiveAsync(toB.Id, None);
            Assert.Equal(ClinicalEncounterStatus.Received, encounter.Status);

            // B cannot send the patient on before care starts there (Received -> Transferred is not a transition).
            var early = await Assert.ThrowsAsync<ResourceConflictException>(() =>
                b.Transfers.RequestAsync(encounter.Id, new RequestTransferRequest(clinicC), None));
            Assert.Contains("cannot be transferred", early.Message);

            await b.Records.RecordTriageAsync(encounter.Id, new RecordTriageRequest(TriagePriority.Immediate), None);
            Assert.Equal(ClinicalEncounterStatus.InProgress, encounter.Status);

            // B -> C: the received transfer to B is history and does not block the new one.
            var toC = await b.Transfers.RequestAsync(encounter.Id, new RequestTransferRequest(clinicC, "Cirugía", "Notas"), None);
            Assert.Equal(ClinicalTransferStatus.Requested, toC.Status);
            Assert.Equal(clinicB, toC.OriginClinic!.Id);
            b.Time.Now = b.Time.Now.AddMinutes(5);
            var dispatched = await b.Transfers.DispatchAsync(toC.Id, None);
            Assert.Equal(ClinicalEncounterStatus.Transferred, dispatched.EncounterStatus);

            var c = Staff(store, clinicC, now: b.Time.Now.AddMinutes(30));
            // B cannot receive its own outgoing transfer.
            await Assert.ThrowsAsync<AccessDeniedException>(() => b.Transfers.ReceiveAsync(toC.Id, None));
            var received = await c.Transfers.ReceiveAsync(toC.Id, None);
            Assert.Equal(ClinicalEncounterStatus.Received, received.EncounterStatus);

            await c.Records.RecordVitalSignAsync(encounter.Id, new RecordVitalSignRequest(VitalSignType.HeartRate, 90, c.Time.Now), None);
            Assert.Equal(ClinicalEncounterStatus.InProgress, encounter.Status);
            await c.Encounters.CloseAsync(encounter.Id, new CloseEncounterRequest("Alta (dato de prueba)."), None);

            Assert.Equal(ClinicalEncounterStatus.Closed, encounter.Status);
            Assert.Single(store.Encounters);
            Assert.Equal(clinicC, encounter.ClinicId);
            Assert.Equal(nnCode, encounter.TemporaryPatientCode!.Value);
            // Nothing recorded along the way was lost: A's note, B's triage, C's vital sign and discharge note.
            Assert.Equal(2, store.Notes.Count(n => n.ClinicalEncounterId == encounter.Id));
            Assert.Single(store.Triages, t => t.ClinicalEncounterId == encounter.Id);
            Assert.Single(store.VitalSigns, v => v.ClinicalEncounterId == encounter.Id);
            Assert.Equal(2, store.Transfers.Count(t => t.Status == ClinicalTransferStatus.Received));
        }

        [Fact]
        public async Task DestinationsExcludeTheCurrentClinicAndInactiveClinics()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Doctor);
            var (other, inactive) = (Guid.NewGuid(), Guid.NewGuid());
            harness.Store.Clinics[harness.ClinicId] = ClinicStatus.Active;
            harness.Store.Clinics[other] = ClinicStatus.Active;
            harness.Store.Clinics[inactive] = ClinicStatus.Inactive;

            var destinations = await harness.Transfers.ListDestinationsAsync(null, None);

            Assert.Equal(other, Assert.Single(destinations).Id);
            Assert.Equal(destinations, await harness.Transfers.ListDestinationsAsync(harness.ClinicId, None));
        }

        [Fact]
        public async Task DestinationsNeedTheClinicWithSeveralAffiliationsAndOnlyTheActorsOnes()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Doctor, clinics: 2);
            var mine = (await harness.Staff.FindByIdentityUserAsync(harness.User.UserId!.Value, None))!.ActiveClinicIds.ToList();
            foreach (var clinic in mine)
            {
                harness.Store.Clinics[clinic] = ClinicStatus.Active;
            }

            await Assert.ThrowsAsync<RequestValidationException>(() => harness.Transfers.ListDestinationsAsync(null, None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => harness.Transfers.ListDestinationsAsync(Guid.NewGuid(), None));
            var fromFirst = await harness.Transfers.ListDestinationsAsync(mine[0], None);
            Assert.Equal(mine[1], Assert.Single(fromFirst).Id);
        }

        [Fact]
        public async Task NoClinicalStaffNoDestinationsEvenForAnAdministrator()
        {
            var admin = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Administrator));
            admin.Store.Clinics[Guid.NewGuid()] = ClinicStatus.Active;

            await Assert.ThrowsAsync<AccessDeniedException>(() => admin.Transfers.ListDestinationsAsync(null, None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => admin.Transfers.ListForEncounterAsync(Guid.NewGuid(), None));
        }

        [Fact]
        public async Task EncounterTransfersShowHistoryAndTheActiveOneNewestFirstOnlyToReaders()
        {
            var store = new FakeClinicalStore();
            var (clinicA, clinicB, clinicC) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            foreach (var clinic in new[] { clinicA, clinicB, clinicC })
            {
                store.Clinics[clinic] = ClinicStatus.Active;
            }

            var a = Staff(store, clinicA);
            var encounter = a.SeedEncounter(clinicId: clinicA, patientId: a.SeedPatient().Id);
            encounter.ChangeStatus(ClinicalEncounterStatus.InProgress, ClinicalHarness.Start);
            var toB = await a.Transfers.RequestAsync(encounter.Id, new RequestTransferRequest(clinicB), None);
            a.Time.Now = a.Time.Now.AddMinutes(5);
            await a.Transfers.DispatchAsync(toB.Id, None);
            var b = Staff(store, clinicB, now: a.Time.Now.AddMinutes(30));
            await b.Transfers.ReceiveAsync(toB.Id, None);
            await RecordAsync(b, encounter.Id, "vital", Guid.Empty);
            b.Time.Now = b.Time.Now.AddMinutes(10);
            var toC = await b.Transfers.RequestAsync(encounter.Id, new RequestTransferRequest(clinicC), None);

            var history = await b.Transfers.ListForEncounterAsync(encounter.Id, None);

            Assert.Equal([toC.Id, toB.Id], history.Select(t => t.Id));
            Assert.Equal([ClinicalTransferStatus.Requested, ClinicalTransferStatus.Received], history.Select(t => t.Status));
            // A handed the patient over: it no longer reads the encounter; an unrelated clinic never did.
            await Assert.ThrowsAsync<AccessDeniedException>(() => a.Transfers.ListForEncounterAsync(encounter.Id, None));
            await Assert.ThrowsAsync<AccessDeniedException>(() => Staff(store, Guid.NewGuid()).Transfers.ListForEncounterAsync(encounter.Id, None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => b.Transfers.ListForEncounterAsync(Guid.NewGuid(), None));
        }

        [Fact]
        public async Task CancellingTheSecondTransferReturnsTheEncounterToCareAtTheSecondHospital()
        {
            var store = new FakeClinicalStore();
            var (clinicA, clinicB) = (Guid.NewGuid(), Guid.NewGuid());
            store.Clinics[clinicA] = ClinicStatus.Active;
            store.Clinics[clinicB] = ClinicStatus.Active;
            var b = Staff(store, clinicB);
            var encounter = ReceivedAt(b);
            await RecordAsync(b, encounter.Id, "note", Guid.Empty);
            var requested = await b.Transfers.RequestAsync(encounter.Id, new RequestTransferRequest(clinicA), None);
            b.Time.Now = b.Time.Now.AddMinutes(5);
            await b.Transfers.DispatchAsync(requested.Id, None);

            var cancelled = await b.Transfers.CancelAsync(requested.Id, None);

            Assert.Equal(ClinicalTransferStatus.Cancelled, cancelled.Status);
            Assert.Equal(ClinicalEncounterStatus.InProgress, encounter.Status);
            Assert.Equal(b.ClinicId, encounter.ClinicId);
            await Assert.ThrowsAsync<ResourceConflictException>(() => b.Transfers.CancelAsync(requested.Id, None));
        }
    }
}
