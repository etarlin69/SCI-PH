using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Common.Biometrics;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Encounters;
using SCI_PH.Domain.Enums.Organizations;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Tests.Clinical
{
    // Identification of an NN emergency by fingerprint (EmergencyFingerprintService for the paramedic,
    // FingerprintReaderService for the reader): a session per capture, bound to one emergency, one reader and one
    // paramedic, expiring after 90 s; the reader reports what the sensor matched; only the paramedic's confirmation
    // identifies the emergency. Also the handoff rule of the mobile identification (document and fingerprint).
    public class EmergencyFingerprintTests
    {
        private static readonly CancellationToken None = CancellationToken.None;
        private static readonly DateTimeOffset StartedAt = ClinicalHarness.Start.AddMinutes(-10);
        private const int Slot = 7;

        private sealed record Scenario(ClinicalHarness Paramedic, ClinicalEncounter Emergency, DeviceSummary Reader, Patient Patient, PatientBiometricReference Reference);

        // An NN emergency, a reader, and a patient whose fingerprint is in slot 7 of that reader.
        private static async Task<Scenario> ScenarioAsync()
        {
            var harness = ClinicalHarness.ForRole(SecurityRoles.Paramedic);
            var opened = await harness.Emergencies.OpenAsync(new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), StartedAt, "Inconsciente"), None);
            var reader = harness.Readers.Add("ESP32-AMB-01");
            var patient = Patient.Register("Ana", "Pérez", new DateOnly(1985, 3, 14), BiologicalSex.Female);
            harness.Store.Patients.Add(patient);
            var reference = PatientBiometricReference.Register(
                patient.Id, BiometricModality.Fingerprint, FingerprintReferences.Provider, FingerprintReferences.SubjectReference(reader.Id, Slot));
            harness.Store.BiometricReferences.Add(reference);
            return new Scenario(harness, harness.Store.Encounters.Single(e => e.Id == opened.Emergency.Id), reader, patient, reference);
        }

        private static async Task<FingerprintSessionSummary> OpenAsync(Scenario s, Guid? key = null, Guid? readerId = null) =>
            (await s.Paramedic.Fingerprints.OpenSessionAsync(s.Emergency.Id, new OpenFingerprintSessionRequest(key ?? Guid.NewGuid(), readerId ?? s.Reader.Id), None)).Session;

        private static Task SubmitAsync(Scenario s, Guid sessionId, FingerprintCaptureOutcome outcome, int? slot = null, DeviceSummary? reader = null) =>
            s.Paramedic.ReaderSide.SubmitResultAsync(
                (reader ?? s.Reader).Id, (reader ?? s.Reader).DeviceCode, sessionId, new SubmitFingerprintResultRequest(outcome, slot), None);

        private static async Task<FingerprintSessionSummary> MatchedAsync(Scenario s)
        {
            var session = await OpenAsync(s);
            await SubmitAsync(s, session.Id, FingerprintCaptureOutcome.Match, Slot);
            return session;
        }

        private static Task<PrehospitalEmergencySummary> ConfirmAsync(Scenario s, Guid sessionId, Guid? key = null) =>
            s.Paramedic.Fingerprints.ConfirmAsync(s.Emergency.Id, sessionId, new ConfirmFingerprintSessionRequest(key ?? Guid.NewGuid()), None);

        private static FingerprintSession Stored(Scenario s, Guid sessionId) => s.Paramedic.Store.Sessions.Single(x => x.Id == sessionId);

        [Fact]
        public async Task OnlyActiveReadersWithTheFingerprintScopeAreListedWithoutSecrets()
        {
            var s = await ScenarioAsync();
            s.Paramedic.Readers.Add("ESP32-REVOKED", DeviceCredentialStatus.Revoked);
            s.Paramedic.Readers.Add("ESP32-HEARTBEAT", DeviceCredentialStatus.Active, DeviceScopes.Heartbeat);
            var second = s.Paramedic.Readers.Add("ESP32-AMB-00");

            var readers = await s.Paramedic.Fingerprints.ListReadersAsync(None);

            Assert.Equal([second.Id, s.Reader.Id], readers.Select(r => r.Id));
            Assert.Equal(new FingerprintReaderSummary(s.Reader.Id, "ESP32-AMB-01", "Lector ESP32-AMB-01", null), readers[1]);
            var noStaff = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic), s.Paramedic.Store);
            await Assert.ThrowsAsync<AccessDeniedException>(() => noStaff.Fingerprints.ListReadersAsync(None));
        }

        [Fact]
        public async Task OpeningASessionIsPendingForNinetySecondsAuditedAndIdempotent()
        {
            var s = await ScenarioAsync();
            var key = Guid.NewGuid();

            var opened = await s.Paramedic.Fingerprints.OpenSessionAsync(s.Emergency.Id, new OpenFingerprintSessionRequest(key, s.Reader.Id), None);

            Assert.True(opened.Created);
            Assert.Equal((FingerprintSessionStatus.Pending, s.Reader.Id, ClinicalHarness.Start.AddSeconds(90), (FingerprintCandidate?)null),
                (opened.Session.Status, opened.Session.ReaderId, opened.Session.ExpiresAtUtc, opened.Session.Candidate));
            var stored = Stored(s, opened.Session.Id);
            Assert.Equal((s.Emergency.Id, s.Paramedic.StaffId), (stored.ClinicalEncounterId, stored.RequestedByStaffId));
            var audit = Assert.Single(s.Paramedic.Store.Audit, a => a.EntityName == EmergencyFingerprintService.SessionEntityName);
            Assert.Equal((AuditActionType.Create, opened.Session.Id.ToString(), (Guid?)null), (audit.Action, audit.EntityId, audit.PatientId));
            Assert.Contains(s.Emergency.Id, s.Paramedic.Store.GuardedEncounters);

            var replay = await s.Paramedic.Fingerprints.OpenSessionAsync(s.Emergency.Id, new OpenFingerprintSessionRequest(key, s.Reader.Id), None);
            Assert.False(replay.Created);
            Assert.Equal(opened.Session, replay.Session);
            Assert.Single(s.Paramedic.Store.Sessions);

            var otherReader = s.Paramedic.Readers.Add("ESP32-AMB-02");
            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                s.Paramedic.Fingerprints.OpenSessionAsync(s.Emergency.Id, new OpenFingerprintSessionRequest(key, otherReader.Id), None));
            // Another key while this session is active: one active session per emergency.
            await Assert.ThrowsAsync<ResourceConflictException>(() => OpenAsync(s, readerId: otherReader.Id));
            Assert.Single(s.Paramedic.Store.Sessions);
        }

        [Fact]
        public async Task OnlyAnActiveFingerprintReaderAndAnUnidentifiedEmergencyCanOpenASession()
        {
            var s = await ScenarioAsync();
            var revoked = s.Paramedic.Readers.Add("ESP32-REVOKED", DeviceCredentialStatus.Revoked);
            var heartbeatOnly = s.Paramedic.Readers.Add("ESP32-HEARTBEAT", DeviceCredentialStatus.Active, DeviceScopes.Heartbeat);

            foreach (var readerId in new[] { Guid.NewGuid(), revoked.Id, heartbeatOnly.Id })
            {
                await Assert.ThrowsAsync<ResourceNotFoundException>(() => OpenAsync(s, readerId: readerId));
            }

            await Assert.ThrowsAsync<RequestValidationException>(() => OpenAsync(s, key: Guid.Empty));
            await Assert.ThrowsAsync<RequestValidationException>(() => OpenAsync(s, readerId: Guid.Empty));
            s.Emergency.IdentifyPatient(s.Patient.Id, s.Paramedic.StaffId, ClinicalHarness.Start);
            await Assert.ThrowsAsync<ResourceConflictException>(() => OpenAsync(s));
            Assert.Empty(s.Paramedic.Store.Sessions);
        }

        // One Pending session per reader; an expired one frees the emergency and the reader.
        [Fact]
        public async Task AReaderServesOneSessionAtATimeAndAnExpiredSessionFreesIt()
        {
            var s = await ScenarioAsync();
            var first = await OpenAsync(s);
            var other = await s.Paramedic.Emergencies.OpenAsync(new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), StartedAt, "Caída"), None);

            await Assert.ThrowsAsync<ResourceConflictException>(() =>
                s.Paramedic.Fingerprints.OpenSessionAsync(other.Emergency.Id, new OpenFingerprintSessionRequest(Guid.NewGuid(), s.Reader.Id), None));

            s.Paramedic.Time.Now = ClinicalHarness.Start.AddSeconds(90);
            var second = await s.Paramedic.Fingerprints.OpenSessionAsync(other.Emergency.Id, new OpenFingerprintSessionRequest(Guid.NewGuid(), s.Reader.Id), None);

            Assert.Equal(FingerprintSessionStatus.Expired, Stored(s, first.Id).Status);
            Assert.Equal(FingerprintSessionStatus.Pending, second.Session.Status);
            Assert.Equal(second.Session.Id, (await s.Paramedic.ReaderSide.GetPendingAsync(s.Reader.Id, None))!.SessionId);
        }

        // The reader learns only the session id and its expiry; another reader sees nothing.
        [Fact]
        public async Task TheReaderOnlySeesItsOwnPendingSession()
        {
            var s = await ScenarioAsync();
            var session = await OpenAsync(s);
            var other = s.Paramedic.Readers.Add("ESP32-AMB-02");

            Assert.Equal(new PendingFingerprintSession(session.Id, session.ExpiresAtUtc), await s.Paramedic.ReaderSide.GetPendingAsync(s.Reader.Id, None));
            Assert.Null(await s.Paramedic.ReaderSide.GetPendingAsync(other.Id, None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => SubmitAsync(s, session.Id, FingerprintCaptureOutcome.NoMatch, reader: other));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => SubmitAsync(s, Guid.NewGuid(), FingerprintCaptureOutcome.NoMatch));

            s.Paramedic.Time.Now = session.ExpiresAtUtc;
            Assert.Null(await s.Paramedic.ReaderSide.GetPendingAsync(s.Reader.Id, None));
        }

        // A match resolves the slot to the reference of THIS reader: Matched with one successful attempt. The emergency is
        // still NN until the paramedic confirms.
        [Fact]
        public async Task AMatchOfAKnownSlotIsRecordedButDoesNotIdentifyTheEmergency()
        {
            var s = await ScenarioAsync();
            var session = await OpenAsync(s);
            s.Paramedic.Time.Now = ClinicalHarness.Start.AddSeconds(5);

            await SubmitAsync(s, session.Id, FingerprintCaptureOutcome.Match, Slot);

            var stored = Stored(s, session.Id);
            Assert.Equal((FingerprintSessionStatus.Matched, (Guid?)s.Reference.Id, (DateTimeOffset?)ClinicalHarness.Start.AddSeconds(5)),
                (stored.Status, stored.MatchedBiometricReferenceId, stored.ResolvedAtUtc));
            Assert.Matches("^[0-9a-f]{64}$", stored.ResultHash);
            var attempt = Assert.Single(s.Paramedic.Store.Attempts);
            Assert.Equal(
                (IdentificationMethod.Fingerprint, IdentificationOutcome.Success, (Guid?)s.Patient.Id, (Guid?)s.Reference.Id, (Guid?)s.Emergency.Id),
                (attempt.Method, attempt.Outcome, attempt.PatientId, attempt.BiometricReferenceId, attempt.ClinicalEncounterId));
            Assert.Equal(("ESP32-AMB-01", (Guid?)s.Paramedic.StaffId, "R307", session.Id.ToString("D")),
                (attempt.DeviceReference, attempt.PerformedByStaffId, attempt.Provider, attempt.ProviderRequestReference));
            Assert.Null(s.Emergency.PatientId);

            var status = await s.Paramedic.Fingerprints.GetSessionAsync(s.Emergency.Id, session.Id, None);
            Assert.Equal((FingerprintSessionStatus.Matched, new FingerprintCandidate("Ana Pérez", new DateOnly(1985, 3, 14))), (status.Status, status.Candidate));
        }

        // The same result again (a lost 204) is accepted without a second attempt; another result is a conflict.
        [Fact]
        public async Task AnIdenticalResultIsAReplayAndAChangedResultIsRejected()
        {
            var s = await ScenarioAsync();
            var session = await OpenAsync(s);
            await SubmitAsync(s, session.Id, FingerprintCaptureOutcome.Match, Slot);
            var resolved = Stored(s, session.Id);

            await SubmitAsync(s, session.Id, FingerprintCaptureOutcome.Match, Slot);
            foreach (var (outcome, slot) in new (FingerprintCaptureOutcome, int?)[]
            {
                (FingerprintCaptureOutcome.Match, Slot + 1),
                (FingerprintCaptureOutcome.NoMatch, null),
                (FingerprintCaptureOutcome.LowQuality, null),
                (FingerprintCaptureOutcome.SensorError, null)
            })
            {
                await Assert.ThrowsAsync<ResourceConflictException>(() => SubmitAsync(s, session.Id, outcome, slot));
            }

            Assert.Single(s.Paramedic.Store.Attempts);
            Assert.Equal(resolved, Stored(s, session.Id));

            // After the confirmation the same lost result can still be repeated safely.
            await ConfirmAsync(s, session.Id);
            await SubmitAsync(s, session.Id, FingerprintCaptureOutcome.Match, Slot);
            Assert.Single(s.Paramedic.Store.Attempts);
        }

        // No usable candidate: a slot without an active reference of this reader is not trusted.
        [Theory]
        [InlineData(FingerprintCaptureOutcome.NoMatch, null, FingerprintSessionStatus.NoMatch, IdentificationFailureReason.NoMatch, null)]
        [InlineData(FingerprintCaptureOutcome.Match, 99, FingerprintSessionStatus.NoMatch, IdentificationFailureReason.NoMatch, null)]
        [InlineData(FingerprintCaptureOutcome.LowQuality, null, FingerprintSessionStatus.Failed, IdentificationFailureReason.LowQualityCapture, FingerprintCaptureOutcome.LowQuality)]
        [InlineData(FingerprintCaptureOutcome.SensorError, null, FingerprintSessionStatus.Failed, IdentificationFailureReason.ProviderUnavailable, FingerprintCaptureOutcome.SensorError)]
        public async Task ResultsWithoutAValidCandidateFailTheAttemptAndKeepTheEmergencyNn(
            FingerprintCaptureOutcome outcome, int? slot, FingerprintSessionStatus expected, IdentificationFailureReason reason, FingerprintCaptureOutcome? failure)
        {
            var s = await ScenarioAsync();
            var session = await OpenAsync(s);

            await SubmitAsync(s, session.Id, outcome, slot);

            var stored = Stored(s, session.Id);
            Assert.Equal((expected, (IdentificationFailureReason?)reason, (Guid?)null), (stored.Status, stored.FailureReason, stored.MatchedBiometricReferenceId));
            var attempt = Assert.Single(s.Paramedic.Store.Attempts);
            Assert.Equal((IdentificationOutcome.Failure, (IdentificationFailureReason?)reason, (Guid?)null), (attempt.Outcome, attempt.FailureReason, attempt.PatientId));
            var status = await s.Paramedic.Fingerprints.GetSessionAsync(s.Emergency.Id, session.Id, None);
            Assert.Equal((expected, failure, (FingerprintCandidate?)null), (status.Status, status.Failure, status.Candidate));
            await Assert.ThrowsAsync<ResourceConflictException>(() => ConfirmAsync(s, session.Id));
            Assert.Null(s.Emergency.PatientId);
        }

        // The slot of another reader is not a match here: a reference belongs to the reader it was enrolled on.
        [Fact]
        public async Task TheSameSlotOnAnotherReaderIsNotAMatch()
        {
            var s = await ScenarioAsync();
            var other = s.Paramedic.Readers.Add("ESP32-AMB-02");
            var session = await OpenAsync(s, readerId: other.Id);

            await SubmitAsync(s, session.Id, FingerprintCaptureOutcome.Match, Slot, other);

            Assert.Equal(FingerprintSessionStatus.NoMatch, Stored(s, session.Id).Status);
        }

        [Theory]
        [InlineData(FingerprintCaptureOutcome.Match, null)]
        [InlineData(FingerprintCaptureOutcome.Match, -1)]
        [InlineData(FingerprintCaptureOutcome.NoMatch, 1)]
        [InlineData(FingerprintCaptureOutcome.LowQuality, 0)]
        [InlineData((FingerprintCaptureOutcome)0, null)]
        public async Task AnInvalidResultIsRejectedWithoutWriting(FingerprintCaptureOutcome outcome, int? slot)
        {
            var s = await ScenarioAsync();
            var session = await OpenAsync(s);

            await Assert.ThrowsAsync<RequestValidationException>(() => SubmitAsync(s, session.Id, outcome, slot));

            Assert.Equal(FingerprintSessionStatus.Pending, Stored(s, session.Id).Status);
            Assert.Empty(s.Paramedic.Store.Attempts);
        }

        // At 90 s the session is over for everyone: the reader's late result, the paramedic's view and the confirmation.
        [Fact]
        public async Task AnExpiredSessionRejectsALateResultAndReadsAsExpired()
        {
            var s = await ScenarioAsync();
            var pending = await OpenAsync(s);
            s.Paramedic.Time.Now = pending.ExpiresAtUtc;

            await Assert.ThrowsAsync<ResourceConflictException>(() => SubmitAsync(s, pending.Id, FingerprintCaptureOutcome.Match, Slot));

            Assert.Equal(FingerprintSessionStatus.Expired, Stored(s, pending.Id).Status);
            Assert.Equal(FingerprintSessionStatus.Expired, (await s.Paramedic.Fingerprints.GetSessionAsync(s.Emergency.Id, pending.Id, None)).Status);
            Assert.Empty(s.Paramedic.Store.Attempts);

            // A match waits for the confirmation only until the same expiry.
            s.Paramedic.Time.Now = ClinicalHarness.Start.AddMinutes(10);
            var matched = await MatchedAsync(s);
            s.Paramedic.Time.Now = matched.ExpiresAtUtc.AddSeconds(1);
            await Assert.ThrowsAsync<ResourceConflictException>(() => ConfirmAsync(s, matched.Id));
            Assert.Equal(FingerprintSessionStatus.Expired, Stored(s, matched.Id).Status);
            Assert.Null(s.Emergency.PatientId);
        }

        // The confirmation identifies the SAME emergency with the reference's patient and the paramedic as the one who
        // identified it, through the clinical identification (Identify audit), and ends the session; a retry replays.
        [Fact]
        public async Task ConfirmingTheCandidateIdentifiesTheEmergencyOnceAndIsIdempotent()
        {
            var s = await ScenarioAsync();
            var session = await MatchedAsync(s);
            var key = Guid.NewGuid();
            s.Paramedic.Time.Now = ClinicalHarness.Start.AddSeconds(20);

            var identified = await ConfirmAsync(s, session.Id, key);

            Assert.Equal((s.Emergency.Id, s.Patient.Id), (identified.Id, identified.Patient!.Id));
            Assert.Equal((s.Patient.Id, (Guid?)s.Paramedic.StaffId), (s.Emergency.PatientId, s.Emergency.IdentifiedByStaffId));
            Assert.NotNull(s.Emergency.TemporaryPatientCode);
            var stored = Stored(s, session.Id);
            Assert.Equal((FingerprintSessionStatus.Confirmed, (DateTimeOffset?)ClinicalHarness.Start.AddSeconds(20)), (stored.Status, stored.ConfirmedAtUtc));
            var audit = Assert.Single(s.Paramedic.Store.Audit, a => a.Action == AuditActionType.Identify);
            Assert.Equal((s.Patient.Id, s.Emergency.Id), (audit.PatientId!.Value, audit.ClinicalEncounterId!.Value));
            Assert.DoesNotContain("r307", audit.ChangesJson!, StringComparison.OrdinalIgnoreCase);

            var replay = await ConfirmAsync(s, session.Id, key);
            Assert.Equal(identified, replay);
            Assert.Single(s.Paramedic.Store.Audit, a => a.Action == AuditActionType.Identify);
            await Assert.ThrowsAsync<ResourceConflictException>(() => ConfirmAsync(s, session.Id));
            Assert.Equal(FingerprintSessionStatus.Confirmed, (await s.Paramedic.Fingerprints.GetSessionAsync(s.Emergency.Id, session.Id, None)).Status);
        }

        [Fact]
        public async Task OnlyAMatchedSessionWithAnActiveReferenceOfAnNnEmergencyCanBeConfirmed()
        {
            var s = await ScenarioAsync();
            var pending = await OpenAsync(s);
            await Assert.ThrowsAsync<ResourceConflictException>(() => ConfirmAsync(s, pending.Id));
            await s.Paramedic.Fingerprints.CancelSessionAsync(s.Emergency.Id, pending.Id, None);

            // Revoked between the match and the confirmation: no candidate, no identification.
            var matched = await MatchedAsync(s);
            // PatientBiometricReference takes its creation time from the real clock.
            s.Reference.Revoke(DateTimeOffset.UtcNow);
            Assert.Null((await s.Paramedic.Fingerprints.GetSessionAsync(s.Emergency.Id, matched.Id, None)).Candidate);
            await Assert.ThrowsAsync<ResourceConflictException>(() => ConfirmAsync(s, matched.Id));
            Assert.Equal(FingerprintSessionStatus.Matched, Stored(s, matched.Id).Status);
            Assert.Null(s.Emergency.PatientId);
        }

        // Identified by document while the fingerprint session waited: the confirmation loses and changes nothing.
        [Fact]
        public async Task AConfirmationAfterAnotherIdentificationIsAConflict()
        {
            var s = await ScenarioAsync();
            var session = await MatchedAsync(s);
            var other = Patient.Register("Luis", "Gómez", new DateOnly(1990, 1, 1), BiologicalSex.Male);
            other.AddIdentifier(PatientIdentifierType.NationalId, "001-1234567-8", PatientIdentifierSource.Manual, isPrimary: true);
            s.Paramedic.Store.Patients.Add(other);
            await s.Paramedic.Documents.ConfirmAsync(
                s.Emergency.Id, new IdentifyEmergencyByDocumentRequest(Guid.NewGuid(), PatientIdentifierType.NationalId, "00112345678"), None);

            await Assert.ThrowsAsync<ResourceConflictException>(() => ConfirmAsync(s, session.Id));

            Assert.Equal(other.Id, s.Emergency.PatientId);
            Assert.Equal(FingerprintSessionStatus.Matched, Stored(s, session.Id).Status);
            Assert.Single(s.Paramedic.Store.Audit, a => a.Action == AuditActionType.Identify);
        }

        // Only the paramedic who opened the session sees and uses it, and only on its emergency.
        [Fact]
        public async Task ASessionBelongsToItsEmergencyAndToThePararamedicWhoOpenedIt()
        {
            var s = await ScenarioAsync();
            var session = await MatchedAsync(s);
            var otherEmergency = await s.Paramedic.Emergencies.OpenAsync(new OpenPrehospitalEmergencyRequest(Guid.NewGuid(), StartedAt, "Caída"), None);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => s.Paramedic.Fingerprints.GetSessionAsync(otherEmergency.Emergency.Id, session.Id, None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
                s.Paramedic.Fingerprints.ConfirmAsync(otherEmergency.Emergency.Id, session.Id, new ConfirmFingerprintSessionRequest(Guid.NewGuid()), None));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => s.Paramedic.Fingerprints.GetSessionAsync(s.Emergency.Id, Guid.NewGuid(), None));

            var otherParamedic = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Paramedic), s.Paramedic.Store);
            otherParamedic.Staff.Add(otherParamedic.User.UserId!.Value, true, Guid.NewGuid());
            await Assert.ThrowsAsync<AccessDeniedException>(() => otherParamedic.Fingerprints.GetSessionAsync(s.Emergency.Id, session.Id, None));
            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                otherParamedic.Fingerprints.ConfirmAsync(s.Emergency.Id, session.Id, new ConfirmFingerprintSessionRequest(Guid.NewGuid()), None));
            await Assert.ThrowsAsync<AccessDeniedException>(() =>
                otherParamedic.Fingerprints.OpenSessionAsync(s.Emergency.Id, new OpenFingerprintSessionRequest(Guid.NewGuid(), s.Reader.Id), None));
            Assert.Null(s.Emergency.PatientId);
        }

        [Fact]
        public async Task CancellingEndsAnActiveSessionOnceAndIsSafeToRetry()
        {
            var s = await ScenarioAsync();
            var pending = await OpenAsync(s);

            var cancelled = await s.Paramedic.Fingerprints.CancelSessionAsync(s.Emergency.Id, pending.Id, None);
            var again = await s.Paramedic.Fingerprints.CancelSessionAsync(s.Emergency.Id, pending.Id, None);

            Assert.Equal((FingerprintSessionStatus.Cancelled, FingerprintSessionStatus.Cancelled), (cancelled.Status, again.Status));
            var audit = Assert.Single(s.Paramedic.Store.Audit, a => a.Action == AuditActionType.Update);
            Assert.Equal((EmergencyFingerprintService.SessionEntityName, pending.Id.ToString()), (audit.EntityName, audit.EntityId));
            Assert.Null(await s.Paramedic.ReaderSide.GetPendingAsync(s.Reader.Id, None));
            await Assert.ThrowsAsync<ResourceConflictException>(() => SubmitAsync(s, pending.Id, FingerprintCaptureOutcome.Match, Slot));

            var matched = await MatchedAsync(s);
            Assert.Equal(FingerprintSessionStatus.Cancelled, (await s.Paramedic.Fingerprints.CancelSessionAsync(s.Emergency.Id, matched.Id, None)).Status);
            Assert.Equal(s.Reference.Id, Stored(s, matched.Id).MatchedBiometricReferenceId);
            await Assert.ThrowsAsync<ResourceConflictException>(() => ConfirmAsync(s, matched.Id));

            var expired = await OpenAsync(s);
            s.Paramedic.Time.Now = expired.ExpiresAtUtc;
            Assert.Equal(FingerprintSessionStatus.Expired, (await s.Paramedic.Fingerprints.CancelSessionAsync(s.Emergency.Id, expired.Id, None)).Status);
            Assert.Equal(2, s.Paramedic.Store.Audit.Count(a => a.Action == AuditActionType.Update));

            s.Paramedic.Time.Now = ClinicalHarness.Start.AddMinutes(10);
            var confirmed = await MatchedAsync(s);
            await ConfirmAsync(s, confirmed.Id);
            await Assert.ThrowsAsync<ResourceConflictException>(() => s.Paramedic.Fingerprints.CancelSessionAsync(s.Emergency.Id, confirmed.Id, None));
        }

        // The reader's match commits while the paramedic cancels: the cancellation is decided again on the Matched session
        // and still ends it (no 409 for a benign race), with its audit entry from the state that won.
        [Fact]
        public async Task ACancellationLosingAgainstTheResultCancelsTheMatchedSession()
        {
            var s = await ScenarioAsync();
            var session = await OpenAsync(s);
            var raced = false;
            s.Paramedic.Store.BeforeSave = () =>
            {
                if (raced)
                {
                    return;
                }

                raced = true;
                var index = s.Paramedic.Store.Sessions.FindIndex(x => x.Id == session.Id);
                s.Paramedic.Store.Sessions[index] = s.Paramedic.Store.Sessions[index] with
                {
                    Status = FingerprintSessionStatus.Matched,
                    MatchedBiometricReferenceId = s.Reference.Id,
                    ResolvedAtUtc = ClinicalHarness.Start,
                    ResultHash = new string('a', 64)
                };
            };

            var cancelled = await s.Paramedic.Fingerprints.CancelSessionAsync(s.Emergency.Id, session.Id, None);

            Assert.Equal(FingerprintSessionStatus.Cancelled, cancelled.Status);
            Assert.Equal((FingerprintSessionStatus.Cancelled, (Guid?)s.Reference.Id), (Stored(s, session.Id).Status, Stored(s, session.Id).MatchedBiometricReferenceId));
            var audit = Assert.Single(s.Paramedic.Store.Audit, a => a.Action == AuditActionType.Update);
            Assert.Contains("Matched", audit.ChangesJson);
        }

        // While the ambulance is on its way (Transferred, no clinic) the patient can still be identified on the mobile.
        [Fact]
        public async Task TheEmergencyCanBeIdentifiedByFingerprintOnTheWay()
        {
            var s = await ScenarioAsync();
            var destination = Guid.NewGuid();
            s.Paramedic.Store.Clinics[destination] = ClinicStatus.Active;
            var requested = await s.Paramedic.Emergencies.RequestTransferAsync(s.Emergency.Id, new RequestEmergencyTransferRequest(Guid.NewGuid(), destination, null, null), None);
            await s.Paramedic.Emergencies.StartTransferAsync(s.Emergency.Id, requested.Transfer.Id, None);
            Assert.Equal((ClinicalEncounterStatus.Transferred, (Guid?)null), (s.Emergency.Status, s.Emergency.ClinicId));

            var session = await MatchedAsync(s);
            await ConfirmAsync(s, session.Id);

            Assert.Equal(s.Patient.Id, s.Emergency.PatientId);
        }

        // Once the hospital received the emergency, the mobile identification is over (document and fingerprint), also
        // for a paramedic affiliated with the receiving clinic; the hospital still identifies through the Web.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task AfterTheHandoffTheMobileIdentificationIsRejected(bool paramedicAffiliatedWithDestination)
        {
            var s = await ScenarioAsync();
            var destination = paramedicAffiliatedWithDestination ? s.Paramedic.ClinicId : Guid.NewGuid();
            s.Paramedic.Store.Clinics[destination] = ClinicStatus.Active;
            var matched = await MatchedAsync(s);
            var document = Patient.Register("Luis", "Gómez", new DateOnly(1990, 1, 1), BiologicalSex.Male);
            document.AddIdentifier(PatientIdentifierType.NationalId, "001-1234567-8", PatientIdentifierSource.Manual, isPrimary: true);
            s.Paramedic.Store.Patients.Add(document);
            var requested = await s.Paramedic.Emergencies.RequestTransferAsync(s.Emergency.Id, new RequestEmergencyTransferRequest(Guid.NewGuid(), destination, null, null), None);
            var started = await s.Paramedic.Emergencies.StartTransferAsync(s.Emergency.Id, requested.Transfer.Id, None);
            var hospital = new ClinicalHarness(FakeCurrentUser.InRole(SecurityRoles.Doctor), s.Paramedic.Store);
            hospital.Staff.Add(hospital.User.UserId!.Value, true, destination);
            await hospital.Transfers.ReceiveAsync(started.Id, None);

            foreach (var write in new Func<Task>[]
            {
                () => s.Paramedic.Documents.ConfirmAsync(
                    s.Emergency.Id, new IdentifyEmergencyByDocumentRequest(Guid.NewGuid(), PatientIdentifierType.NationalId, "00112345678"), None),
                () => ConfirmAsync(s, matched.Id),
                () => s.Paramedic.Fingerprints.CancelSessionAsync(s.Emergency.Id, matched.Id, None),
                () => OpenAsync(s)
            })
            {
                await Assert.ThrowsAsync<ResourceConflictException>(write);
            }

            Assert.Null(s.Emergency.PatientId);
            await hospital.Encounters.IdentifyPatientAsync(s.Emergency.Id, new IdentifyEncounterPatientRequest(document.Id), None);
            Assert.Equal(document.Id, s.Emergency.PatientId);
        }

        // ---- The session's reader must still be a fingerprint reader (ADR-009, revalidation at Confirm) ----

        // What the reader became after the match: revoked, deleted, or without the Identify scope.
        public static TheoryData<string> InvalidReaders => new() { "Revoked", "Missing", "WithoutIdentify" };

        private static void InvalidateReader(Scenario s, string how)
        {
            var devices = s.Paramedic.Readers.Devices;
            var index = devices.FindIndex(d => d.Id == s.Reader.Id);
            switch (how)
            {
                case "Revoked":
                    devices[index] = s.Reader with { Status = DeviceCredentialStatus.Revoked, RevokedAtUtc = ClinicalHarness.Start };
                    break;
                case "Missing":
                    devices.RemoveAt(index);
                    break;
                default:
                    devices[index] = s.Reader with { Scopes = [DeviceScopes.FingerprintEnroll] };
                    break;
            }
        }

        // The match was valid, but its reader no longer is: no candidate (without saying why), Confirm 409 every time with
        // the same key, nothing written (no patient, no Identify entry, no receipt), the session and the reference unchanged.
        // The session can still be cancelled, and the emergency identified by another reader or by document.
        [Theory]
        [MemberData(nameof(InvalidReaders))]
        public async Task AMatchFromAReaderThatIsNoLongerValidCannotBeConfirmed(string how)
        {
            var s = await ScenarioAsync();
            var matched = await MatchedAsync(s);
            Assert.NotNull((await s.Paramedic.Fingerprints.GetSessionAsync(s.Emergency.Id, matched.Id, None)).Candidate);
            var saves = s.Paramedic.Store.SaveCount;

            InvalidateReader(s, how);

            var shown = await s.Paramedic.Fingerprints.GetSessionAsync(s.Emergency.Id, matched.Id, None);
            Assert.Equal((FingerprintSessionStatus.Matched, (FingerprintCandidate?)null), (shown.Status, shown.Candidate));
            var key = Guid.NewGuid();
            var first = await Assert.ThrowsAsync<ResourceConflictException>(() => ConfirmAsync(s, matched.Id, key));
            var again = await Assert.ThrowsAsync<ResourceConflictException>(() => ConfirmAsync(s, matched.Id, key));
            Assert.Equal(first.Message, again.Message);
            Assert.DoesNotContain("revoked", first.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("scope", first.Message, StringComparison.OrdinalIgnoreCase);

            Assert.Equal(saves, s.Paramedic.Store.SaveCount);
            Assert.Null(s.Emergency.PatientId);
            Assert.DoesNotContain(s.Paramedic.Store.Audit, a => a.Action == AuditActionType.Identify);
            Assert.DoesNotContain(s.Paramedic.Store.Receipts, r => r.Key.Operation == EmergencyFingerprintService.ConfirmOperation);
            Assert.Equal(FingerprintSessionStatus.Matched, Stored(s, matched.Id).Status);
            Assert.Equal(BiometricReferenceStatus.Active, s.Reference.Status);

            // Released by the paramedic (the app cancels it quietly): another reader can be used at once.
            Assert.Equal(FingerprintSessionStatus.Cancelled, (await s.Paramedic.Fingerprints.CancelSessionAsync(s.Emergency.Id, matched.Id, None)).Status);
            var other = s.Paramedic.Readers.Add("ESP32-AMB-02");
            Assert.Equal(FingerprintSessionStatus.Pending, (await OpenAsync(s, readerId: other.Id)).Status);

            // The document identification is untouched.
            var document = s.Paramedic.SeedPatient();
            var identified = await s.Paramedic.Documents.ConfirmAsync(
                s.Emergency.Id, new IdentifyEmergencyByDocumentRequest(Guid.NewGuid(), PatientIdentifierType.NationalId, "00112345678"), None);
            Assert.Equal(document.Id, s.Emergency.PatientId);
            Assert.NotNull(identified.Patient);
        }

        // Without any identification the emergency simply stays NN: the rejected confirmation changes nothing else.
        [Fact]
        public async Task AfterARejectedConfirmationTheEmergencyContinuesAsNN()
        {
            var s = await ScenarioAsync();
            var matched = await MatchedAsync(s);
            InvalidateReader(s, "Revoked");

            await Assert.ThrowsAsync<ResourceConflictException>(() => ConfirmAsync(s, matched.Id));

            var emergency = await s.Paramedic.Emergencies.GetAsync(s.Emergency.Id, None);
            Assert.Null(emergency.Patient);
            Assert.NotNull(s.Emergency.TemporaryPatientCode);
            Assert.DoesNotContain(s.Emergency.Status, ClinicalEncounter.FinalStatuses);
        }

        // Only the session's own reader matters: revoking another reader changes nothing for it.
        [Fact]
        public async Task RevokingAnotherReaderDoesNotAffectTheConfirmation()
        {
            var s = await ScenarioAsync();
            var other = s.Paramedic.Readers.Add("ESP32-AMB-02");
            var matched = await MatchedAsync(s);
            var index = s.Paramedic.Readers.Devices.FindIndex(d => d.Id == other.Id);
            s.Paramedic.Readers.Devices[index] = other with { Status = DeviceCredentialStatus.Revoked };

            Assert.NotNull((await s.Paramedic.Fingerprints.GetSessionAsync(s.Emergency.Id, matched.Id, None)).Candidate);
            await ConfirmAsync(s, matched.Id);

            Assert.Equal(s.Patient.Id, s.Emergency.PatientId);
            Assert.Equal(FingerprintSessionStatus.Confirmed, Stored(s, matched.Id).Status);
        }

        // A confirmation committed while the reader was valid stays a success: its replay after a later revocation returns
        // the identified emergency (never a retroactive error).
        [Fact]
        public async Task AConfirmationCommittedBeforeTheRevocationReplaysAsASuccess()
        {
            var s = await ScenarioAsync();
            var matched = await MatchedAsync(s);
            var key = Guid.NewGuid();
            var confirmed = await ConfirmAsync(s, matched.Id, key);

            InvalidateReader(s, "Revoked");
            var replay = await ConfirmAsync(s, matched.Id, key);

            Assert.Equal(confirmed, replay);
            Assert.Equal(s.Patient.Id, s.Emergency.PatientId);
            Assert.Single(s.Paramedic.Store.Audit, a => a.Action == AuditActionType.Identify);
        }
    }
}
