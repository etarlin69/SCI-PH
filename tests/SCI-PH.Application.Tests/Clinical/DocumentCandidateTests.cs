using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Common.Biometrics;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Tests.Clinical
{
    // Identification by document in two steps (EmergencyDocumentIdentificationService): resolve shows a minimized candidate
    // and changes nothing; only the confirmation, which resolves the document again on the server, identifies the SAME
    // emergency, with exactly one successful IdentificationAttempt.
    public class DocumentCandidateTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private const string NationalId = "001-1234567-8";
        private const string Passport = "RD1234567";

        private static ResolveEmergencyDocumentRequest Resolve(string number = NationalId, PatientIdentifierType type = PatientIdentifierType.NationalId) =>
            new(type, number);

        private static IdentifyEmergencyByDocumentRequest Confirm(
            string number = NationalId, PatientIdentifierType type = PatientIdentifierType.NationalId, Guid? key = null) =>
            new(key ?? Guid.NewGuid(), type, number);

        private static async Task<(ClinicalHarness Harness, ClinicalEncounter Emergency)> OpenedAsync()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var opened = await harness.Emergencies.OpenAsync(
                new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), ClinicalHarness.Start.AddMinutes(-10), "Inconsciente"), None);
            return (harness, harness.Store.Encounters.Single(e => e.Id == opened.Emergency.Id));
        }

        private static Patient Seed(ClinicalHarness harness, PatientIdentifierType type = PatientIdentifierType.NationalId, string value = NationalId,
            string given = "Ana", string family = "Pérez")
        {
            var patient = Patient.Register(given, family, new DateOnly(1980, 1, 1), BiologicalSex.Female);
            patient.AddIdentifier(type, value, PatientIdentifierSource.Manual, isPrimary: true);
            harness.Store.Patients.Add(patient);
            return patient;
        }

        private static PatientIdentifier IdentifierOf(Patient patient, PatientIdentifierType type) =>
            patient.Identifiers.Single(i => i.Type == type && i.IsActive);

        // ---- Resolve ----

        [Theory]
        [InlineData(PatientIdentifierType.NationalId, NationalId)]
        [InlineData(PatientIdentifierType.Passport, Passport)]
        public async Task ResolveReturnsOnlyTheMinimizedCandidateAndChangesNothing(PatientIdentifierType type, string number)
        {
            var (harness, emergency) = await OpenedAsync();
            var patient = Seed(harness, type, number);
            var code = emergency.TemporaryPatientCode!.Value;

            var candidate = await harness.Documents.ResolveAsync(emergency.Id, Resolve(number, type), None);

            Assert.Equal("Ana Pérez", candidate.DisplayName);
            Assert.Equal(new DateOnly(1980, 1, 1), candidate.DateOfBirth);
            Assert.Equal(type, candidate.Document.Type);
            var normalized = PatientIdentifier.Normalize(type, number);
            // Masked as every patient summary masks a document: only its last characters remain.
            Assert.EndsWith(normalized[^4..], candidate.Document.Value);
            Assert.DoesNotContain(normalized, candidate.Document.Value);
            Assert.True(candidate.Document.Value[..^4].All(c => c == '*'), candidate.Document.Value);
            // Only what a human confirmation needs: name, date of birth, masked document.
            Assert.Equal(
                [nameof(PatientDocumentCandidate.DisplayName), nameof(PatientDocumentCandidate.DateOfBirth), nameof(PatientDocumentCandidate.Document)],
                typeof(PatientDocumentCandidate).GetProperties().Select(p => p.Name).Where(n => n != "EqualityContract").ToArray());
            Assert.DoesNotContain(patient.Id.ToString(), candidate.ToString());

            // The emergency stays NN: no identification, no attempt, no receipt.
            Assert.Null(emergency.PatientId);
            Assert.Equal(code, emergency.TemporaryPatientCode!.Value);
            Assert.Empty(harness.Store.Attempts);
            Assert.DoesNotContain(harness.Store.Receipts, r => r.Key.Operation == EmergencyDocumentIdentificationService.ConfirmOperation);
            Assert.DoesNotContain(harness.Store.Audit, a => a.Action == AuditActionType.Identify);

            // Candidate discovery is a Read audit entry on the identifier: never the document itself.
            var read = Assert.Single(harness.Store.Audit, a => a.EntityName == EmergencyDocumentIdentificationService.CandidateAuditResource);
            Assert.Equal((AuditActionType.Read, IdentifierOf(patient, type).Id.ToString(), (Guid?)patient.Id, (Guid?)emergency.Id),
                (read.Action, read.EntityId, read.PatientId, read.ClinicalEncounterId));
            Assert.DoesNotContain(harness.Store.Audit, a => (a.ChangesJson ?? string.Empty).Contains(normalized));
        }

        [Fact]
        public async Task ResolvingTwiceStillLeavesTheEmergencyNn()
        {
            var (harness, emergency) = await OpenedAsync();
            Seed(harness);

            await harness.Documents.ResolveAsync(emergency.Id, Resolve(), None);
            await harness.Documents.ResolveAsync(emergency.Id, Resolve("00112345678"), None);

            Assert.Null(emergency.PatientId);
            Assert.Empty(harness.Store.Attempts);
        }

        // An unknown document is the generic 404, recorded as a failed attempt without patient or document.
        [Fact]
        public async Task ResolvingAnUnknownDocumentIsTheGenericNotFound()
        {
            var (harness, emergency) = await OpenedAsync();

            var error = await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                harness.Documents.ResolveAsync(emergency.Id, Resolve(Passport, PatientIdentifierType.Passport), None));

            Assert.DoesNotContain(Passport, error.Message);
            Assert.Null(emergency.PatientId);
            var miss = Assert.Single(harness.Store.Attempts);
            Assert.Equal((IdentificationMethod.Passport, IdentificationOutcome.Failure, IdentificationFailureReason.NoMatch, (Guid?)null, (Guid?)null, (Guid?)emergency.Id),
                (miss.Method, miss.Outcome, miss.FailureReason, miss.PatientId, miss.PatientIdentifierId, miss.ClinicalEncounterId));
            Assert.DoesNotContain(harness.Store.Audit, a => a.EntityName == EmergencyDocumentIdentificationService.CandidateAuditResource);
        }

        // Inactive identifiers and identifiers of another issuer never resolve: the same 404 as an unknown document.
        [Fact]
        public async Task AnIdentifierOutsideThePlatformScopeIsNotACandidate()
        {
            var (harness, emergency) = await OpenedAsync();
            var foreign = Patient.Register("Luis", "Gómez", null, BiologicalSex.Male);
            foreign.AddIdentifier(PatientIdentifierType.Passport, Passport, PatientIdentifierSource.Manual, "DO", isPrimary: true);
            harness.Store.Patients.Add(foreign);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                harness.Documents.ResolveAsync(emergency.Id, Resolve(Passport, PatientIdentifierType.Passport), None));
        }

        [Theory]
        [InlineData(PatientIdentifierType.InternalCode)]
        [InlineData(PatientIdentifierType.Other)]
        public async Task ResolveAcceptsOnlyNationalIdAndPassport(PatientIdentifierType type)
        {
            var (harness, emergency) = await OpenedAsync();

            var error = await Assert.ThrowsAsync<RequestValidationException>(() =>
                harness.Documents.ResolveAsync(emergency.Id, Resolve("SCI-1", type), None));

            Assert.Equal("documentType", error.Field);
            Assert.Empty(harness.Store.Attempts);
        }

        [Theory]
        [InlineData("")]
        [InlineData("--- ...")]
        public async Task ResolveRejectsAnEmptyDocument(string number)
        {
            var (harness, emergency) = await OpenedAsync();

            var error = await Assert.ThrowsAsync<RequestValidationException>(() => harness.Documents.ResolveAsync(emergency.Id, Resolve(number), None));

            Assert.Equal("documentNumber", error.Field);
            Assert.Empty(harness.Store.Attempts);
        }

        // Access to the emergency before the lookup: another paramedic learns nothing about who holds a document.
        [Fact]
        public async Task AnotherParamedicCannotResolveAndLeavesNoTrace()
        {
            var (harness, emergency) = await OpenedAsync();
            Seed(harness);
            var other = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic), harness.Store);
            other.Staff.Add(other.User.UserId!.Value, true, Guid.NewGuid());

            await Assert.ThrowsAsync<AccessDeniedException>(() => other.Documents.ResolveAsync(emergency.Id, Resolve(), None));
            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                other.Documents.ResolveAsync(emergency.Id, Resolve("00100000000"), None));

            Assert.Empty(harness.Store.Attempts);
            Assert.DoesNotContain(harness.Store.Audit, a => a.EntityName == EmergencyDocumentIdentificationService.CandidateAuditResource);
        }

        [Fact]
        public async Task AnIdentifiedEmergencyHasNoCandidate()
        {
            var (harness, emergency) = await OpenedAsync();
            Seed(harness);
            Seed(harness, PatientIdentifierType.Passport, Passport, "Luis", "Gómez");
            await harness.Documents.ConfirmAsync(emergency.Id, Confirm(), None);

            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                harness.Documents.ResolveAsync(emergency.Id, Resolve(Passport, PatientIdentifierType.Passport), None));
        }

        // The candidate shows personal data: without its audit entry it is not returned.
        [Fact]
        public async Task ACandidateWhoseReadCannotBeAuditedIsNotReturned()
        {
            var (harness, emergency) = await OpenedAsync();
            Seed(harness);
            harness.Store.BeforeSave = () => throw new InvalidOperationException("database down (fake)");

            await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => harness.Documents.ResolveAsync(emergency.Id, Resolve(), None));

            Assert.DoesNotContain(harness.DocumentLogs.Lines, l => l.Contains("5678") || l.Contains("Pérez"));
            Assert.Contains(harness.DocumentLogs.Lines, l => l.Contains("InvalidOperationException"));
        }

        // ---- Confirm ----

        [Theory]
        [InlineData(PatientIdentifierType.NationalId, NationalId, IdentificationMethod.NationalId)]
        [InlineData(PatientIdentifierType.Passport, Passport, IdentificationMethod.Passport)]
        public async Task ConfirmIdentifiesTheSameEncounterWithExactlyOneSuccessfulAttempt(
            PatientIdentifierType type, string number, IdentificationMethod method)
        {
            var (harness, emergency) = await OpenedAsync();
            var patient = Seed(harness, type, number);
            var code = emergency.TemporaryPatientCode!.Value;
            await harness.Documents.ResolveAsync(emergency.Id, Resolve(number, type), None);

            var result = await harness.Documents.ConfirmAsync(emergency.Id, Confirm(number, type), None);

            var encounter = Assert.Single(harness.Store.Encounters);
            Assert.Equal((emergency.Id, (Guid?)patient.Id, code), (encounter.Id, encounter.PatientId, encounter.TemporaryPatientCode!.Value));
            Assert.Equal((emergency.Id, code, patient.Id), (result.Id, result.TemporaryPatientCode, result.Patient!.Id));
            var attempt = Assert.Single(harness.Store.Attempts);
            var actor = await harness.Actor.GetAsync(None);
            Assert.Equal(
                (method, IdentificationOutcome.Success, (IdentificationFailureReason?)null, (Guid?)patient.Id, (Guid?)IdentifierOf(patient, type).Id,
                    (Guid?)emergency.Id, (Guid?)actor.StaffId, (Guid?)actor.IdentityUserId, (Guid?)null),
                (attempt.Method, attempt.Outcome, attempt.FailureReason, attempt.PatientId, attempt.PatientIdentifierId,
                    attempt.ClinicalEncounterId, attempt.PerformedByStaffId, attempt.ActorIdentityUserId, attempt.BiometricReferenceId));
            Assert.Single(harness.Store.Audit, a => a.Action == AuditActionType.Identify);
        }

        // Confirm never depends on a previous resolve and trusts nothing of the candidate: it looks the document up again.
        [Fact]
        public async Task ConfirmResolvesTheDocumentAgainOnTheServer()
        {
            var (harness, emergency) = await OpenedAsync();
            var patient = Seed(harness);
            await harness.Documents.ResolveAsync(emergency.Id, Resolve(), None);
            // The document stopped being valid between resolve and confirm.
            patient.RevokeIdentifier(IdentifierOf(patient, PatientIdentifierType.NationalId).Id, DateTimeOffset.UtcNow);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => harness.Documents.ConfirmAsync(emergency.Id, Confirm(), None));

            Assert.Null(emergency.PatientId);
            var miss = Assert.Single(harness.Store.Attempts);
            Assert.Equal((IdentificationOutcome.Failure, IdentificationFailureReason.NoMatch), (miss.Outcome, miss.FailureReason));
            Assert.DoesNotContain(harness.Store.Receipts, r => r.Key.Operation == EmergencyDocumentIdentificationService.ConfirmOperation);
        }

        [Fact]
        public async Task AReplayedConfirmIdentifiesOnceWithOneSuccessfulAttempt()
        {
            var (harness, emergency) = await OpenedAsync();
            var patient = Seed(harness);
            var key = Guid.NewGuid();
            await harness.Documents.ConfirmAsync(emergency.Id, Confirm(key: key), None);
            var saves = harness.Store.SaveCount;

            // Same key, same document (another spelling of it): the same answer, nothing new.
            var again = await harness.Documents.ConfirmAsync(emergency.Id, Confirm("00112345678", key: key), None);

            Assert.Equal((emergency.Id, patient.Id), (again.Id, again.Patient!.Id));
            Assert.Equal(saves, harness.Store.SaveCount);
            Assert.Single(harness.Store.Attempts);
            Assert.Single(harness.Store.Audit, a => a.Action == AuditActionType.Identify);
        }

        // Already identified (here by another document) when the confirmation arrives: 409, nothing overwritten.
        [Fact]
        public async Task AnAlreadyIdentifiedEmergencyIsAConflictAndKeepsItsPatient()
        {
            var (harness, emergency) = await OpenedAsync();
            Seed(harness);
            var first = Seed(harness, PatientIdentifierType.Passport, Passport, "Luis", "Gómez");
            await harness.Documents.ResolveAsync(emergency.Id, Resolve(), None);
            await harness.Documents.ConfirmAsync(emergency.Id, Confirm(Passport, PatientIdentifierType.Passport), None);

            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Documents.ConfirmAsync(emergency.Id, Confirm(), None));

            Assert.Equal(first.Id, emergency.PatientId);
            Assert.Single(harness.Store.Attempts, a => a.Outcome == IdentificationOutcome.Success);
        }

        // Cross-method: candidate A resolved by document, then the fingerprint identifies patient B, then the document
        // confirmation arrives: no overwrite, one final identity, one successful attempt (the fingerprint's).
        [Fact]
        public async Task ADocumentConfirmationAfterAFingerprintIdentificationDoesNotOverwrite()
        {
            var (harness, emergency) = await OpenedAsync();
            Seed(harness);
            var byFingerprint = Patient.Register("Luis", "Gómez", new DateOnly(1990, 1, 1), BiologicalSex.Male);
            harness.Store.Patients.Add(byFingerprint);
            var reader = harness.Readers.Add("ESP32-AMB-01");
            harness.Store.BiometricReferences.Add(PatientBiometricReference.Register(
                byFingerprint.Id, BiometricModality.Fingerprint, FingerprintReferences.Provider, FingerprintReferences.SubjectReference(reader.Id, 7)));
            var candidate = await harness.Documents.ResolveAsync(emergency.Id, Resolve(), None);
            Assert.Equal("Ana Pérez", candidate.DisplayName);

            var session = (await harness.Fingerprints.OpenSessionAsync(
                emergency.Id, new OpenFingerprintSessionRequest(Guid.NewGuid(), reader.Id), None)).Session;
            await harness.ReaderSide.SubmitResultAsync(
                reader.Id, reader.DeviceCode, session.Id, new SubmitFingerprintResultRequest(FingerprintCaptureOutcome.Match, 7), None);
            await harness.Fingerprints.ConfirmAsync(emergency.Id, session.Id, new ConfirmFingerprintSessionRequest(Guid.NewGuid()), None);

            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Documents.ConfirmAsync(emergency.Id, Confirm(), None));

            Assert.Equal(byFingerprint.Id, emergency.PatientId);
            var success = Assert.Single(harness.Store.Attempts, a => a.Outcome == IdentificationOutcome.Success);
            Assert.Equal(IdentificationMethod.Fingerprint, success.Method);
            Assert.Single(harness.Store.Audit, a => a.Action == AuditActionType.Identify);
        }

        // After the handoff neither step is offered: 409 for the paramedic who took part, nothing written.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task AfterTheHandoffResolveAndConfirmAreRejected(bool paramedicAffiliatedWithDestination)
        {
            var (harness, emergency) = await OpenedAsync();
            Seed(harness);
            var destination = paramedicAffiliatedWithDestination ? harness.ClinicId : Guid.NewGuid();
            harness.Store.Clinics[destination] = ClinicStatus.Active;
            var requested = await harness.Emergencies.RequestTransferAsync(
                emergency.Id, new RequestEmergencyTransferRequest(Guid.NewGuid(), destination, null, null), None);
            var started = await harness.Emergencies.StartTransferAsync(emergency.Id, requested.Transfer.Id, None);
            var hospital = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Doctor), harness.Store);
            hospital.Staff.Add(hospital.User.UserId!.Value, true, destination);
            await hospital.Transfers.ReceiveAsync(started.Id, None);

            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Documents.ResolveAsync(emergency.Id, Resolve(), None));
            await Assert.ThrowsAsync<ResourceConflictException>(() => harness.Documents.ConfirmAsync(emergency.Id, Confirm(), None));

            Assert.Null(emergency.PatientId);
            Assert.Empty(harness.Store.Attempts);
        }

        // NN in transport: identifying keeps the SAME transfer, in its status.
        [Fact]
        public async Task ConfirmDuringTheTransferKeepsTheTransfer()
        {
            var (harness, emergency) = await OpenedAsync();
            var patient = Seed(harness);
            var destination = Guid.NewGuid();
            harness.Store.Clinics[destination] = ClinicStatus.Active;
            var requested = await harness.Emergencies.RequestTransferAsync(
                emergency.Id, new RequestEmergencyTransferRequest(Guid.NewGuid(), destination, null, null), None);
            var started = await harness.Emergencies.StartTransferAsync(emergency.Id, requested.Transfer.Id, None);

            await harness.Documents.ResolveAsync(emergency.Id, Resolve(), None);
            var result = await harness.Documents.ConfirmAsync(emergency.Id, Confirm(), None);

            Assert.Equal(patient.Id, result.Patient!.Id);
            var transfer = Assert.Single(harness.Store.Transfers);
            Assert.Equal((started.Id, Domain.Enums.Transport.ClinicalTransferStatus.InTransit), (transfer.Id, transfer.Status));
            var current = await harness.Emergencies.GetAsync(emergency.Id, None);
            Assert.Equal((started.Id, Domain.Enums.Transport.ClinicalTransferStatus.InTransit), (current.ActiveTransfer!.Id, current.ActiveTransfer.Status));
        }

        [Fact]
        public void TheRequestsNeverPrintTheDocument()
        {
            Assert.DoesNotContain(NationalId, Resolve().ToString());
            Assert.DoesNotContain(NationalId, Confirm().ToString());
        }
    }
}
