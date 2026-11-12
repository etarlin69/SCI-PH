using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Tests.Clinical
{
    // Identification of the NN patient of a prehospital emergency by document (PrehospitalEmergencyService over
    // EncounterService.IdentifyPatientAsync): platform scope only, same encounter, idempotent, never creates a patient.
    public class DocumentIdentificationTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        private static IdentifyEmergencyByDocumentRequest Document(
            string number = "001-1234567-8", PatientIdentifierType type = PatientIdentifierType.NationalId, Guid? clientRequestId = null) =>
            new(clientRequestId ?? Guid.NewGuid(), type, number);

        private static async Task<(ClinicalHarness Harness, ClinicalEncounter Emergency)> OpenedEmergencyAsync()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var opened = await harness.Emergencies.OpenAsync(
                new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), ClinicalHarness.Start.AddMinutes(-10), "Dolor torácico"), None);
            return (harness, harness.Store.Encounters.Single(e => e.Id == opened.Emergency.Id));
        }

        private static Patient SeedPatient(ClinicalHarness harness, PatientIdentifierType type, string value, string? issuer = null)
        {
            var patient = Patient.Register("Ana", "Pérez", new DateOnly(1980, 1, 1), BiologicalSex.Female);
            patient.AddIdentifier(type, value, PatientIdentifierSource.Manual, issuer, isPrimary: true);
            harness.Store.Patients.Add(patient);
            return patient;
        }

        [Theory]
        [InlineData(PatientIdentifierType.NationalId, "001-1234567-8")]
        [InlineData(PatientIdentifierType.Passport, "RD1234567")]
        public async Task TheDocumentIdentifiesTheSameEncounterAndKeepsTheTemporaryCode(PatientIdentifierType type, string number)
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var patient = SeedPatient(harness, type, number);
            var code = emergency.TemporaryPatientCode!.Value;
            var auditsBefore = harness.Store.Audit.Count;
            harness.Time.Now = ClinicalHarness.Start.AddMinutes(5);
            var request = Document(number, type);

            var result = await harness.Documents.ConfirmAsync(emergency.Id, request, None);

            var encounter = Assert.Single(harness.Store.Encounters);
            Assert.Equal(emergency.Id, encounter.Id);
            Assert.Equal(patient.Id, encounter.PatientId);
            Assert.Equal(code, encounter.TemporaryPatientCode!.Value);
            Assert.Null(encounter.ClinicId);
            Assert.Equal(ClinicalEncounterType.Prehospital, encounter.Type);
            Assert.Equal(ClinicalEncounterStatus.Created, encounter.Status);
            Assert.Equal((await harness.Actor.GetAsync(None)).StaffId, encounter.IdentifiedByStaffId);
            Assert.Equal(harness.Time.Now, encounter.IdentifiedAtUtc);
            Assert.Single(harness.Store.Patients);

            var audit = Assert.Single(harness.Store.Audit.Skip(auditsBefore));
            Assert.Equal((AuditActionType.Identify, emergency.Id, patient.Id), (audit.Action, audit.ClinicalEncounterId, audit.PatientId));
            var receipt = harness.Store.Receipts.Single(r => r.Key.Operation == EmergencyDocumentIdentificationService.ConfirmOperation);
            Assert.Equal(new IdempotencyKey(harness.User.UserId!.Value, EmergencyDocumentIdentificationService.ConfirmOperation, request.ClientRequestId), receipt.Key);
            Assert.Equal(emergency.Id, receipt.ResourceId);

            Assert.Equal(emergency.Id, result.Id);
            Assert.Equal(code, result.TemporaryPatientCode);
            Assert.Equal(patient.Id, result.Patient!.Id);
            // The summary masks the document: the number sent never comes back.
            Assert.DoesNotContain(PatientIdentifier.Normalize(type, number), result.Patient.PrimaryIdentifier!.Value);
        }

        [Theory]
        [InlineData("001-1234567-8")]
        [InlineData(" 00112345678 ")]
        [InlineData("001 1234567 8")]
        public async Task EquivalentSpellingsFindTheSamePatient(string number)
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var patient = SeedPatient(harness, PatientIdentifierType.NationalId, "00112345678");

            var result = await harness.Documents.ConfirmAsync(emergency.Id, Document(number), None);

            Assert.Equal(patient.Id, result.Patient!.Id);
        }

        // D1: only the platform scope (empty issuer). An identifier of another issuer is never a match. The miss is only a
        // failed attempt (NoMatch, without patient or document): no receipt, no identification.
        [Fact]
        public async Task AnIdentifierOfAnotherIssuerIsNotFoundAndOnlyTheMissIsRecorded()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            SeedPatient(harness, PatientIdentifierType.Passport, "RD1234567", issuer: "DO");
            var saves = harness.Store.SaveCount;

            var error = await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                harness.Documents.ConfirmAsync(emergency.Id, Document("RD1234567", PatientIdentifierType.Passport), None));

            Assert.DoesNotContain("RD1234567", error.Message);
            Assert.Null(emergency.PatientId);
            Assert.Equal(saves + 1, harness.Store.SaveCount);
            var miss = Assert.Single(harness.Store.Attempts);
            Assert.Equal((IdentificationMethod.Passport, IdentificationOutcome.Failure, IdentificationFailureReason.NoMatch, (Guid?)null, (Guid?)emergency.Id),
                (miss.Method, miss.Outcome, miss.FailureReason, miss.PatientId, miss.ClinicalEncounterId));
            Assert.DoesNotContain(harness.Store.Receipts, r => r.Key.Operation == EmergencyDocumentIdentificationService.ConfirmOperation);
            Assert.DoesNotContain(harness.Store.Audit, a => a.Action == AuditActionType.Identify);
        }

        [Fact]
        public async Task ThePlatformPatientWinsOverTheSameValueOfAnotherIssuer()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            SeedPatient(harness, PatientIdentifierType.Passport, "RD1234567", issuer: "DO");
            var platform = SeedPatient(harness, PatientIdentifierType.Passport, "RD1234567");

            var result = await harness.Documents.ConfirmAsync(emergency.Id, Document("RD1234567", PatientIdentifierType.Passport), None);

            Assert.Equal(platform.Id, result.Patient!.Id);
        }

        // Not found writes nothing, so the same request can succeed once the patient exists (no success receipt).
        [Fact]
        public async Task AnUnknownDocumentLeavesTheEmergencyNnAndCanBeRetriedLater()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var request = Document("001-7654321-0");

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Documents.ConfirmAsync(emergency.Id, request, None));
            Assert.Null(emergency.PatientId);
            Assert.Empty(harness.Store.Patients);

            var patient = SeedPatient(harness, PatientIdentifierType.NationalId, "001-7654321-0");
            var result = await harness.Documents.ConfirmAsync(emergency.Id, request, None);
            Assert.Equal(patient.Id, result.Patient!.Id);
        }

        [Theory]
        [InlineData(PatientIdentifierType.Other)]
        [InlineData(PatientIdentifierType.QrCode)]
        [InlineData(PatientIdentifierType.InternalCode)]
        [InlineData(PatientIdentifierType.Phone)]
        [InlineData(PatientIdentifierType.Email)]
        [InlineData((PatientIdentifierType)99)]
        public async Task OnlyNationalIdAndPassportAreAccepted(PatientIdentifierType type)
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            SeedPatient(harness, PatientIdentifierType.InternalCode, "SCI-1");

            var error = await Assert.ThrowsAsync<RequestValidationException>(() =>
                harness.Documents.ConfirmAsync(emergency.Id, Document("SCI-1", type), None));
            Assert.Equal("documentType", error.Field);
            Assert.Null(emergency.PatientId);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("--- ...")]
        [InlineData(null)]
        public async Task AnEmptyDocumentIsRejected(string? number)
        {
            var (harness, emergency) = await OpenedEmergencyAsync();

            var error = await Assert.ThrowsAsync<RequestValidationException>(() =>
                harness.Documents.ConfirmAsync(emergency.Id, Document(number!), None));
            Assert.Equal("documentNumber", error.Field);
        }

        [Fact]
        public async Task AClientRequestIdIsRequired()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();

            var error = await Assert.ThrowsAsync<RequestValidationException>(() =>
                harness.Documents.ConfirmAsync(emergency.Id, Document(clientRequestId: Guid.Empty), None));
            Assert.Equal("clientRequestId", error.Field);
        }

        // Same key and same logical payload (also another spelling of the same document): the same result, nothing new.
        [Fact]
        public async Task RetryingReplaysTheIdentificationWithoutAnotherAudit()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var patient = SeedPatient(harness, PatientIdentifierType.NationalId, "001-1234567-8");
            var key = Guid.NewGuid();
            var first = await harness.Documents.ConfirmAsync(emergency.Id, Document("001-1234567-8", clientRequestId: key), None);
            var audits = harness.Store.Audit.Count;
            var saves = harness.Store.SaveCount;

            var again = await harness.Documents.ConfirmAsync(emergency.Id, Document("00112345678", clientRequestId: key), None);

            Assert.Equal((first.Id, patient.Id), (again.Id, again.Patient!.Id));
            Assert.Equal(audits, harness.Store.Audit.Count);
            Assert.Equal(saves, harness.Store.SaveCount);
            Assert.Single(harness.Store.Receipts, r => r.Key.Operation == EmergencyDocumentIdentificationService.ConfirmOperation);
        }

        [Fact]
        public async Task TheSameKeyWithAnotherDocumentTypeOrEmergencyIsAConflict()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var patient = SeedPatient(harness, PatientIdentifierType.NationalId, "001-1234567-8");
            SeedPatient(harness, PatientIdentifierType.NationalId, "001-7654321-0");
            SeedPatient(harness, PatientIdentifierType.Passport, "00112345678");
            var other = await harness.Emergencies.OpenAsync(
                new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), ClinicalHarness.Start.AddMinutes(-5), "Caída"), None);
            var key = Guid.NewGuid();
            await harness.Documents.ConfirmAsync(emergency.Id, Document("001-1234567-8", clientRequestId: key), None);
            var audits = harness.Store.Audit.Count;

            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                harness.Documents.ConfirmAsync(emergency.Id, Document("001-7654321-0", clientRequestId: key), None));
            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                harness.Documents.ConfirmAsync(emergency.Id, Document("00112345678", PatientIdentifierType.Passport, key), None));
            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                harness.Documents.ConfirmAsync(other.Emergency.Id, Document("001-1234567-8", clientRequestId: key), None));

            Assert.Equal(patient.Id, emergency.PatientId);
            Assert.Null(harness.Store.Encounters.Single(e => e.Id == other.Emergency.Id).PatientId);
            Assert.Equal(audits, harness.Store.Audit.Count);
        }

        // D4: a new request on an identified emergency is a 409, even for the same patient.
        [Fact]
        public async Task AnIdentifiedEmergencyRejectsANewRequestEvenForTheSamePatient()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var patient = SeedPatient(harness, PatientIdentifierType.NationalId, "001-1234567-8");
            await harness.Documents.ConfirmAsync(emergency.Id, Document(), None);
            var audits = harness.Store.Audit.Count;

            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                harness.Documents.ConfirmAsync(emergency.Id, Document(), None));

            Assert.Equal(patient.Id, emergency.PatientId);
            Assert.Equal(audits, harness.Store.Audit.Count);
        }

        // The same request lost the race to itself: the winner's receipt answers it.
        [Fact]
        public async Task LosingARaceWithTheSameRequestReturnsTheWinner()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            var patient = SeedPatient(harness, PatientIdentifierType.NationalId, "001-1234567-8");
            var request = Document();
            var key = new IdempotencyKey(harness.User.UserId!.Value, EmergencyDocumentIdentificationService.ConfirmOperation, request.ClientRequestId);
            var winnerHash = Common.Idempotency.IdempotencyRequestHash.Compute(emergency.Id.ToString("D"), "NationalId", "00112345678");
            harness.Store.BeforeSave = () =>
            {
                harness.Store.BeforeSave = null;
                harness.Store.Receipts.Add(new IdempotencyReceipt(key, winnerHash, emergency.Id, ClinicalHarness.Start));
            };

            var result = await harness.Documents.ConfirmAsync(emergency.Id, request, None);

            Assert.Equal((emergency.Id, patient.Id), (result.Id, result.Patient!.Id));
            Assert.Single(harness.Store.Receipts, r => r.Key == key);
            Assert.DoesNotContain(harness.Store.Audit, a => a.Action == AuditActionType.Identify);
        }

        [Fact]
        public async Task OnlyTheAssignedParamedicIdentifiesTheEmergency()
        {
            var (harness, emergency) = await OpenedEmergencyAsync();
            SeedPatient(harness, PatientIdentifierType.NationalId, "001-1234567-8");
            var other = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic), harness.Store);
            other.Staff.Add(other.User.UserId!.Value, true, Guid.NewGuid());

            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                other.Documents.ConfirmAsync(emergency.Id, Document(), None));
            Assert.Null(emergency.PatientId);
        }

        [Fact]
        public async Task AnEncounterThatIsNotPrehospitalIsNotAnEmergency()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            SeedPatient(harness, PatientIdentifierType.NationalId, "001-1234567-8");
            var hospital = harness.SeedEncounter();

            await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                harness.Documents.ConfirmAsync(hospital.Id, Document(), None));
            Assert.Null(hospital.PatientId);
        }

        // D2 regression: a clinic encounter keeps the clinic rule when identified through EncounterService.
        [Fact]
        public async Task AClinicEncounterIsStillIdentifiedByItsClinicStaffOnly()
        {
            var reception = ClinicalHarness.ForRole(SecurityRoles.Receptionist);
            var patient = reception.SeedPatient();
            var encounter = reception.SeedEncounter();
            var otherClinic = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Receptionist), reception.Store);
            otherClinic.Staff.Add(otherClinic.User.UserId!.Value, true, Guid.NewGuid());

            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                otherClinic.Encounters.IdentifyPatientAsync(encounter.Id, new IdentifyEncounterPatientRequest(patient.Id), None));
            await reception.Encounters.IdentifyPatientAsync(encounter.Id, new IdentifyEncounterPatientRequest(patient.Id), None);

            Assert.Equal(patient.Id, encounter.PatientId);
        }
    }
}
