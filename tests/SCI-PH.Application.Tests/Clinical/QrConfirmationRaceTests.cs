using Microsoft.Extensions.Logging.Abstractions;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Authorization;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Tests.Clinical
{
    // Same-key race of the QR confirmation (issue #53): the window between the receipt lookup and the emergency load.
    // A double submit whose first request committed inside that window replays it (same 200, nothing new); anything that is
    // not this same request (other data under the key, another key, another method) stays a 409.
    public class QrConfirmationRaceTests
    {
        private static readonly CancellationToken None = CancellationToken.None;

        private static async Task<(ClinicalHarness Harness, EmergencyQrIdentificationService Qr, ClinicalEncounter Emergency)> OpenedAsync()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var qr = new EmergencyQrIdentificationService(
                harness.Actor, harness.Emergencies, harness.Encounters, new FakeQrCredentials(harness.Store), new NoSelfReads(),
                new PatientAccessAuthorizer(harness.User, new NoPatientLinks()), harness.Store, harness.Store, harness.Time,
                NullLogger<EmergencyQrIdentificationService>.Instance);
            var opened = await harness.Emergencies.OpenAsync(
                new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), ClinicalHarness.Start.AddMinutes(-10), "Inconsciente"), None);
            return (harness, qr, harness.Store.Encounters.Single(e => e.Id == opened.Emergency.Id));
        }

        // A patient with a valid system-issued QR; returns the payload the patient would show.
        private static (Patient Patient, string Payload) SeedWithQr(ClinicalHarness harness, string given = "Ana", string family = "Pérez")
        {
            var patient = Patient.Register(given, family, new DateOnly(1980, 1, 1), BiologicalSex.Female);
            var (payload, hash) = PatientQrToken.Generate();
            patient.AddIdentifier(PatientIdentifierType.QrCode, hash, PatientIdentifierSource.System, isPrimary: true,
                // The domain checks it against the real clock; the service against the harness clock, which is earlier.
                validUntilUtc: DateTimeOffset.UtcNow + PatientQrToken.Lifetime);
            harness.Store.Patients.Add(patient);
            return (patient, payload);
        }

        private static ConfirmPatientQrRequest Confirm(string payload, Guid key) => new(key, payload);

        // The double submit: this request found no receipt, then the SAME request (same user, key and QR) committed before
        // this one loaded the emergency. It sees the emergency identified, finds the winner's receipt and replays it.
        [Fact]
        public async Task ASameKeyConfirmThatLosesTheRaceReplaysTheWinner()
        {
            var (harness, qr, emergency) = await OpenedAsync();
            var (patient, payload) = SeedWithQr(harness);
            var code = emergency.TemporaryPatientCode!.Value;
            var key = Guid.NewGuid();
            harness.Store.AfterReceiptLookup = () =>
            {
                harness.Store.AfterReceiptLookup = null;
                qr.ConfirmAsync(emergency.Id, Confirm(payload, key), None).GetAwaiter().GetResult();
            };

            var loser = await qr.ConfirmAsync(emergency.Id, Confirm(payload, key), None);

            Assert.Equal((emergency.Id, patient.Id), (loser.Id, loser.Patient!.Id));
            Assert.Equal(code, emergency.TemporaryPatientCode!.Value);
            var success = Assert.Single(harness.Store.Attempts);
            Assert.Equal((IdentificationOutcome.Success, IdentificationMethod.QrCode), (success.Outcome, success.Method));
            Assert.Single(harness.Store.Receipts, r => r.Key.Operation == EmergencyQrIdentificationService.ConfirmOperation);
            Assert.Single(harness.Store.Audit, a => a.Action == AuditActionType.Identify);
        }

        // The same key reused with ANOTHER QR in that window is not the same request: 409, never a replay of the winner.
        [Fact]
        public async Task ASameKeyConfirmWithAnotherQrThatLosesTheRaceIsAConflict()
        {
            var (harness, qr, emergency) = await OpenedAsync();
            var (_, payload) = SeedWithQr(harness);
            var (winner, otherPayload) = SeedWithQr(harness, "Luis", "Gómez");
            var key = Guid.NewGuid();
            harness.Store.AfterReceiptLookup = () =>
            {
                harness.Store.AfterReceiptLookup = null;
                qr.ConfirmAsync(emergency.Id, Confirm(otherPayload, key), None).GetAwaiter().GetResult();
            };

            var error = await Assert.ThrowsAsync<ResourceConflictException>(() => qr.ConfirmAsync(emergency.Id, Confirm(payload, key), None));

            Assert.Contains("different data", error.Message);
            Assert.Equal(winner.Id, emergency.PatientId);
            Assert.Single(harness.Store.Attempts, a => a.Outcome == IdentificationOutcome.Success);
            Assert.Single(harness.Store.Receipts, r => r.Key.Operation == EmergencyQrIdentificationService.ConfirmOperation);
        }

        // Another METHOD identifying in that window leaves no QR receipt of this request: the second lookup finds nothing and
        // the original conflict stands (409, the document's patient kept, no QR receipt created).
        [Fact]
        public async Task ADocumentIdentificationInsideTheRaceWindowIsStillAConflict()
        {
            var (harness, qr, emergency) = await OpenedAsync();
            var (_, payload) = SeedWithQr(harness);
            var byDocument = Patient.Register("Luis", "Gómez", new DateOnly(1990, 1, 1), BiologicalSex.Male);
            byDocument.AddIdentifier(PatientIdentifierType.NationalId, "001-1234567-8", PatientIdentifierSource.Manual, isPrimary: true);
            harness.Store.Patients.Add(byDocument);
            harness.Store.AfterReceiptLookup = () =>
            {
                harness.Store.AfterReceiptLookup = null;
                harness.Documents.ConfirmAsync(emergency.Id,
                    new IdentifyEmergencyByDocumentRequest(Guid.NewGuid(), PatientIdentifierType.NationalId, "001-1234567-8"), None)
                    .GetAwaiter().GetResult();
            };

            var error = await Assert.ThrowsAsync<ResourceConflictException>(() => qr.ConfirmAsync(emergency.Id, Confirm(payload, Guid.NewGuid()), None));

            Assert.Equal("The emergency is already identified.", error.Message);
            Assert.Equal(byDocument.Id, emergency.PatientId);
            var success = Assert.Single(harness.Store.Attempts, a => a.Outcome == IdentificationOutcome.Success);
            Assert.Equal(IdentificationMethod.NationalId, success.Method);
            Assert.DoesNotContain(harness.Store.Receipts, r => r.Key.Operation == EmergencyQrIdentificationService.ConfirmOperation);
            Assert.Single(harness.Store.Audit, a => a.Action == AuditActionType.Identify);
        }

        // A DIFFERENT QR request (same QR, another clientRequestId) winning in that window has no receipt under this key:
        // the original 409 is preserved, not turned into a replay.
        [Fact]
        public async Task AnotherQrRequestInsideTheRaceWindowIsStillAConflict()
        {
            var (harness, qr, emergency) = await OpenedAsync();
            var (patient, payload) = SeedWithQr(harness);
            var winnerKey = Guid.NewGuid();
            harness.Store.AfterReceiptLookup = () =>
            {
                harness.Store.AfterReceiptLookup = null;
                qr.ConfirmAsync(emergency.Id, Confirm(payload, winnerKey), None).GetAwaiter().GetResult();
            };

            var error = await Assert.ThrowsAsync<ResourceConflictException>(() => qr.ConfirmAsync(emergency.Id, Confirm(payload, Guid.NewGuid()), None));

            Assert.Equal("The emergency is already identified.", error.Message);
            Assert.Equal(patient.Id, emergency.PatientId);
            Assert.Single(harness.Store.Attempts, a => a.Outcome == IdentificationOutcome.Success);
            var receipt = Assert.Single(harness.Store.Receipts, r => r.Key.Operation == EmergencyQrIdentificationService.ConfirmOperation);
            Assert.Equal(winnerKey, receipt.Key.ClientRequestId);
        }

        // The system-issued QR credentials of the in-memory store's patients, looked up by hash as the real store does.
        private sealed class FakeQrCredentials(FakeClinicalStore store) : IPatientQrCredentialStore
        {
            public Task<Patient?> LoadPatientWithActiveQrAsync(Guid patientId, CancellationToken cancellationToken) =>
                Task.FromResult(store.Patients.SingleOrDefault(p => p.Id == patientId));

            public void Add(PatientIdentifier credential) => throw new NotSupportedException();

            public Task<PatientIdentifier?> FindByHashAsync(string hash, CancellationToken cancellationToken) =>
                Task.FromResult(store.Patients.SelectMany(p => p.Identifiers).SingleOrDefault(i =>
                    i.Type == PatientIdentifierType.QrCode && i.Source == PatientIdentifierSource.System && i.NormalizedValue == hash));
        }

        // The confirmation never reads the patient's own profile (only resolve shows the candidate).
        private sealed class NoSelfReads : IPatientSelfReadStore
        {
            public Task<PatientSelfProfileRow?> GetProfileAsync(Guid patientId, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<int> CountEncountersAsync(Guid patientId, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<IReadOnlyList<PatientSelfEncounter>> ListEncountersAsync(Guid patientId, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<PatientSelfEncounter?> GetEncounterAsync(Guid patientId, Guid encounterId, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<IReadOnlyList<PatientSelfAllergy>> ListAllergiesAsync(Guid patientId, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<IReadOnlyList<PatientSelfDiagnosis>> ListDiagnosesAsync(Guid patientId, Guid? encounterId, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<IReadOnlyList<PatientSelfPrescription>> ListPrescriptionsAsync(Guid patientId, Guid? encounterId, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<int> CountResultsAsync(Guid patientId, CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<IReadOnlyList<PatientSelfResult>> ListResultsAsync(Guid patientId, Guid? encounterId, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        }

        private sealed class NoPatientLinks : IClinicalIdentityLinkService
        {
            public Task<ClinicalSubject?> FindSubjectAsync(InvitationSubjectType type, Guid subjectId, CancellationToken cancellationToken) =>
                Task.FromResult<ClinicalSubject?>(null);

            public Task<bool> LinkAsync(InvitationSubjectType type, Guid subjectId, Guid userId, CancellationToken cancellationToken) =>
                Task.FromResult(false);

            public Task<Guid?> FindPatientIdByUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
        }
    }
}
