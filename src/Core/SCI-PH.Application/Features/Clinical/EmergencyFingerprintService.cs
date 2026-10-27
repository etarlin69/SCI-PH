using System.Text.Json;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Biometrics;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Features.Clinical
{
    // Identification of the NN patient of a prehospital emergency by fingerprint (Mobile BFF, ADR-009). The paramedic
    // chooses a reader and opens a session; the reader captures and matches on the sensor and reports through the Device
    // API (FingerprintReaderService); the paramedic sees the candidate and confirms. A match never identifies the
    // emergency by itself: only the confirmation does, through EncounterService.IdentifyPatientAsync (access, Domain rule,
    // Identify audit entry, PatientId concurrency token), with the paramedic as IdentifiedByStaffId and the patient taken
    // from the session's biometric reference (the app never sends a patient id). Every step is a prehospital mobile write:
    // only before the hospital received the emergency (PrehospitalEmergencyService.LoadForIdentificationAsync).
    public sealed class EmergencyFingerprintService
    {
        // Server-controlled operation names of the idempotency receipts; a new version gets a new name.
        public const string OpenSessionOperation = "prehospital-emergency.fingerprint-session.v1";
        public const string ConfirmOperation = "prehospital-emergency.fingerprint-confirm.v1";

        public const string SessionEntityName = "FingerprintIdentificationSession";

        private readonly ICurrentClinicalActor _actor;
        private readonly PrehospitalEmergencyService _emergencies;
        private readonly EncounterService _encounters;
        private readonly IClinicalReadStore _reads;
        private readonly IClinicalWriteStore _writes;
        private readonly IIdempotencyStore _receipts;
        private readonly IFingerprintSessionStore _sessions;
        private readonly IDeviceCredentialStore _readers;
        private readonly TimeProvider _time;

        public EmergencyFingerprintService(
            ICurrentClinicalActor actor,
            PrehospitalEmergencyService emergencies,
            EncounterService encounters,
            IClinicalReadStore reads,
            IClinicalWriteStore writes,
            IIdempotencyStore receipts,
            IFingerprintSessionStore sessions,
            IDeviceCredentialStore readers,
            TimeProvider time)
        {
            _actor = actor;
            _emergencies = emergencies;
            _encounters = encounters;
            _reads = reads;
            _writes = writes;
            _receipts = receipts;
            _sessions = sessions;
            _readers = readers;
            _time = time;
        }

        // Active readers with the fingerprint scope, by name. Any of them can be chosen (no reader-ambulance relation yet).
        public async Task<IReadOnlyList<FingerprintReaderSummary>> ListReadersAsync(CancellationToken cancellationToken)
        {
            await _actor.GetAsync(cancellationToken);
            return (await _readers.ListAsync(cancellationToken))
                .Where(IsReader)
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(d => d.DeviceCode, StringComparer.Ordinal)
                .Select(d => new FingerprintReaderSummary(d.Id, d.DeviceCode, d.Name, d.LastSeenAtUtc))
                .ToList();
        }

        // A Pending session of the NN emergency with that reader, its Create audit entry and the receipt, in one transaction.
        // It expires FingerprintReferences.SessionTtl after the server's now. One active session per emergency and one
        // Pending session per reader (unique indexes; an expired one is closed first): 409 otherwise. Unknown reader, revoked
        // or without the fingerprint scope: 404. Same key and same payload again: the same session (200). Same key with
        // another emergency or reader: 409.
        public async Task<OpenFingerprintSessionResult> OpenSessionAsync(
            Guid emergencyId, OpenFingerprintSessionRequest request, CancellationToken cancellationToken)
        {
            var clientRequestId = ClinicalInput.Id(request.ClientRequestId, "clientRequestId");
            var readerId = ClinicalInput.Id(request.DeviceId, "deviceId");
            var actor = await _actor.GetAsync(cancellationToken);
            var key = new IdempotencyKey(actor.IdentityUserId, OpenSessionOperation, clientRequestId);
            var requestHash = IdempotencyRequestHash.Compute(emergencyId.ToString("D"), readerId.ToString("D"));

            if (await _receipts.FindAsync(key, cancellationToken) is { } existing)
            {
                return new OpenFingerprintSessionResult(await ReplayOpenAsync(actor, existing, requestHash, emergencyId, cancellationToken), Created: false);
            }

            var encounter = await _emergencies.LoadForIdentificationAsync(actor, emergencyId, cancellationToken);
            EnsureIdentifiable(encounter);
            var reader = await _readers.FindAsync(readerId, cancellationToken);
            if (reader is null || !IsReader(reader))
            {
                throw new ResourceNotFoundException("FingerprintReader", "No active fingerprint reader was found with this id.");
            }

            var now = _time.GetUtcNow();
            await _sessions.ExpireDueAsync(new FingerprintSessionScope(ClinicalEncounterId: encounter.Id, DeviceCredentialId: reader.Id), now, cancellationToken);
            var session = new FingerprintSession(
                Guid.NewGuid(), encounter.Id, reader.Id, actor.StaffId, FingerprintSessionStatus.Pending,
                MatchedBiometricReferenceId: null, FailureReason: null, ResultHash: null,
                CreatedAtUtc: now, ExpiresAtUtc: now + FingerprintReferences.SessionTtl, ResolvedAtUtc: null, ConfirmedAtUtc: null);
            _sessions.Add(session);
            // A concurrent identification or reception after the check above makes the session lose (409).
            _writes.EnsureEncounterUnchanged(encounter);
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Create, SessionEntityName, session.Id.ToString(), now,
                new AuditContext(ClinicalEncounterId: encounter.Id, ChangesJson: JsonSerializer.Serialize(new
                {
                    readerId = reader.Id,
                    expiresAtUtc = session.ExpiresAtUtc
                }))));
            _receipts.Add(new IdempotencyReceipt(key, requestHash, session.Id, now));

            if (await SaveAsync(key, cancellationToken) is { } winner)
            {
                return new OpenFingerprintSessionResult(await ReplayOpenAsync(actor, winner, requestHash, emergencyId, cancellationToken), Created: false);
            }

            return new OpenFingerprintSessionResult(await SummaryAsync(session, cancellationToken), Created: true);
        }

        // The state of a session of the emergency, only for the paramedic who opened it (404 for anyone else or another
        // emergency). A Pending or Matched session past its expiry is closed as Expired here.
        public async Task<FingerprintSessionSummary> GetSessionAsync(Guid emergencyId, Guid sessionId, CancellationToken cancellationToken)
        {
            var actor = await _actor.GetAsync(cancellationToken);
            await _emergencies.ReadAsync(actor, emergencyId, cancellationToken);
            var session = await CurrentAsync(await OwnSessionAsync(actor, emergencyId, sessionId, cancellationToken), cancellationToken);
            return await SummaryAsync(session, cancellationToken);
        }

        // The paramedic accepts the candidate of a Matched session: the emergency is identified with the patient of the
        // session's reference (still active), the session becomes Confirmed and the receipt is written, all in the
        // transaction of EncounterService.IdentifyPatientAsync (with its Identify audit entry). A concurrent identification
        // (another session, a document), reception or cancellation wins on the encounter's or the session's concurrency
        // token: 409, nothing written. Same key and same payload again: the identified emergency (200), nothing new.
        public async Task<PrehospitalEmergencySummary> ConfirmAsync(
            Guid emergencyId, Guid sessionId, ConfirmFingerprintSessionRequest request, CancellationToken cancellationToken)
        {
            var clientRequestId = ClinicalInput.Id(request.ClientRequestId, "clientRequestId");
            var actor = await _actor.GetAsync(cancellationToken);
            var key = new IdempotencyKey(actor.IdentityUserId, ConfirmOperation, clientRequestId);
            var requestHash = IdempotencyRequestHash.Compute(emergencyId.ToString("D"), sessionId.ToString("D"));

            if (await _receipts.FindAsync(key, cancellationToken) is { } existing)
            {
                return await ReplayConfirmAsync(actor, existing, requestHash, cancellationToken);
            }

            var encounter = await _emergencies.LoadForIdentificationAsync(actor, emergencyId, cancellationToken);
            var session = await OwnSessionAsync(actor, emergencyId, sessionId, cancellationToken);
            var now = _time.GetUtcNow();
            if (session.IsDue(now))
            {
                await _sessions.ExpireDueAsync(new FingerprintSessionScope(SessionId: session.Id), now, cancellationToken);
                throw new ResourceConflictException("The fingerprint session expired. Open a new one.");
            }

            if (session.Status != FingerprintSessionStatus.Matched)
            {
                throw new ResourceConflictException($"A fingerprint session in status {session.Status} cannot be confirmed.");
            }

            var reference = await _writes.FindBiometricReferenceAsync(session.MatchedBiometricReferenceId!.Value, cancellationToken);
            if (reference is not { Status: BiometricReferenceStatus.Active })
            {
                throw new ResourceConflictException("The fingerprint reference of the candidate is no longer active.");
            }

            var patientId = reference.PatientId;
            // The reference must still be active when the identification commits: a revocation committed after this check
            // makes the whole save lose on its Status token (409), so a revoked reference never identifies.
            _writes.EnsureBiometricReferenceUnchanged(reference);
            // Tracked only: written by the SaveChangesAsync of IdentifyPatientAsync, in the same transaction.
            _sessions.Update(session with { Status = FingerprintSessionStatus.Confirmed, ConfirmedAtUtc = now }, FingerprintSessionStatus.Matched);
            // The receipt's resource is the emergency, as for the identification by document: a replay returns it.
            _receipts.Add(new IdempotencyReceipt(key, requestHash, encounter.Id, now));
            try
            {
                await _encounters.IdentifyPatientAsync(encounter.Id, new IdentifyEncounterPatientRequest(patientId), cancellationToken);
            }
            catch (IdempotencyKeyCollisionException)
            {
                // A concurrent request with the same key committed first; this one wrote nothing.
                var winner = await _receipts.FindAsync(key, cancellationToken)
                    ?? throw new InvalidOperationException("The idempotency receipt that won the race was not found.");
                return await ReplayConfirmAsync(actor, winner, requestHash, cancellationToken);
            }
            catch (ResourceConflictException)
            {
                // Already identified, or lost a race: a replay only if this same request is the one that won.
                if (await _receipts.FindAsync(key, cancellationToken) is { } winner)
                {
                    return await ReplayConfirmAsync(actor, winner, requestHash, cancellationToken);
                }

                throw;
            }

            return await _emergencies.ReadAsync(actor, encounter.Id, cancellationToken);
        }

        // Pending or Matched: Cancelled, with an Update audit entry (only on that transition). Already ended without an
        // identification (Cancelled, Expired, NoMatch, Failed): 200 with that state and nothing written, so a retry is safe.
        // Confirmed: 409 (the identification is not undone here). When the reader's result commits first, the session is
        // read again: still active (now Matched), it is cancelled from that state.
        public async Task<FingerprintSessionSummary> CancelSessionAsync(Guid emergencyId, Guid sessionId, CancellationToken cancellationToken)
        {
            var actor = await _actor.GetAsync(cancellationToken);
            var encounter = await _emergencies.LoadForIdentificationAsync(actor, emergencyId, cancellationToken);
            var session = await OwnSessionAsync(actor, emergencyId, sessionId, cancellationToken);
            for (var attempt = 1; ; attempt++)
            {
                session = await CurrentAsync(session, cancellationToken);
                if (session.Status == FingerprintSessionStatus.Confirmed)
                {
                    throw new ResourceConflictException("A confirmed fingerprint session cannot be cancelled.");
                }

                if (!session.IsActive)
                {
                    return await SummaryAsync(session, cancellationToken);
                }

                var now = _time.GetUtcNow();
                var cancelled = session with { Status = FingerprintSessionStatus.Cancelled };
                _sessions.Update(cancelled, session.Status);
                _writes.Add(ClinicalAuditEntry.Success(
                    actor.AuditActor, AuditActionType.Update, SessionEntityName, session.Id.ToString(), now,
                    new AuditContext(encounter.PatientId, encounter.Id, ChangesJson: JsonSerializer.Serialize(new
                    {
                        status = new[] { session.Status.ToString(), cancelled.Status.ToString() }
                    }))));
                try
                {
                    await _writes.SaveChangesAsync(cancellationToken);
                    return await SummaryAsync(cancelled, cancellationToken);
                }
                catch (ResourceConflictException) when (attempt < MaxCancelAttempts)
                {
                    // The reader's result, an expiry or another cancellation committed first; nothing of this one was
                    // written. Decide again on the state that won.
                    session = await _sessions.FindAsync(session.Id, cancellationToken)
                        ?? throw new InvalidOperationException("A fingerprint session disappeared.");
                }
            }
        }

        // Pending -> (result) Matched -> cancelled: two transitions can come first at most.
        private const int MaxCancelAttempts = 3;

        private static bool IsReader(DeviceSummary device) =>
            device.Status == DeviceCredentialStatus.Active && device.Scopes.Contains(DeviceScopes.FingerprintIdentify);

        // Only an NN emergency that is not cancelled or closed is identified here.
        private static void EnsureIdentifiable(ClinicalEncounter encounter)
        {
            if (encounter.IsPatientIdentified)
            {
                throw new ResourceConflictException("The emergency is already identified.");
            }

            if (ClinicalEncounter.FinalStatuses.Contains(encounter.Status))
            {
                throw new ResourceConflictException($"An emergency in status {encounter.Status} cannot be identified.");
            }
        }

        // A session of this emergency opened by this paramedic; any other session is not found, so its existence is not
        // revealed.
        private async Task<FingerprintSession> OwnSessionAsync(
            ClinicalActor actor, Guid emergencyId, Guid sessionId, CancellationToken cancellationToken)
        {
            var session = await _sessions.FindAsync(sessionId, cancellationToken);
            if (session is null || session.ClinicalEncounterId != emergencyId || session.RequestedByStaffId != actor.StaffId)
            {
                throw new ResourceNotFoundException(SessionEntityName, sessionId);
            }

            return session;
        }

        // The session as it stands now: one that is past its expiry is closed as Expired first.
        private async Task<FingerprintSession> CurrentAsync(FingerprintSession session, CancellationToken cancellationToken)
        {
            var now = _time.GetUtcNow();
            if (!session.IsDue(now))
            {
                return session;
            }

            await _sessions.ExpireDueAsync(new FingerprintSessionScope(SessionId: session.Id), now, cancellationToken);
            return await _sessions.FindAsync(session.Id, cancellationToken)
                ?? throw new InvalidOperationException("An expired fingerprint session was not found.");
        }

        private async Task<FingerprintSessionSummary> SummaryAsync(FingerprintSession session, CancellationToken cancellationToken)
        {
            var failure = session.Status == FingerprintSessionStatus.Failed
                ? session.FailureReason == IdentificationFailureReason.LowQualityCapture
                    ? FingerprintCaptureOutcome.LowQuality
                    : FingerprintCaptureOutcome.SensorError
                : (FingerprintCaptureOutcome?)null;
            return new FingerprintSessionSummary(
                session.Id, session.Status, session.DeviceCredentialId, session.ExpiresAtUtc, failure,
                await CandidateAsync(session, cancellationToken));
        }

        // Name and date of birth of the matched patient while the session waits for the confirmation and the reference is
        // still active; nothing else, and never the patient id.
        private async Task<FingerprintCandidate?> CandidateAsync(FingerprintSession session, CancellationToken cancellationToken)
        {
            if (session is not { Status: FingerprintSessionStatus.Matched, MatchedBiometricReferenceId: { } referenceId }
                || await _reads.GetActiveBiometricReferencePatientIdAsync(referenceId, cancellationToken) is not { } patientId
                || await _reads.GetPatientAsync(patientId, cancellationToken) is not { } patient)
            {
                return null;
            }

            return new FingerprintCandidate(ClinicalMapping.DisplayName(patient), patient.DateOfBirth);
        }

        // The receipt's resource is the session; the emergency is the one of the request (the hash covers it). A replay
        // re-checks the read access and returns the session as it is now.
        private async Task<FingerprintSessionSummary> ReplayOpenAsync(
            ClinicalActor actor, IdempotencyReceipt receipt, string requestHash, Guid emergencyId, CancellationToken cancellationToken)
        {
            PrehospitalEmergencyService.EnsureSameRequest(receipt, requestHash);
            await _emergencies.ReadAsync(actor, emergencyId, cancellationToken);
            var session = await _sessions.FindAsync(receipt.ResourceId, cancellationToken)
                ?? throw new InvalidOperationException("The fingerprint session of a receipt was not found.");
            return await SummaryAsync(await CurrentAsync(session, cancellationToken), cancellationToken);
        }

        private async Task<PrehospitalEmergencySummary> ReplayConfirmAsync(
            ClinicalActor actor, IdempotencyReceipt receipt, string requestHash, CancellationToken cancellationToken)
        {
            PrehospitalEmergencyService.EnsureSameRequest(receipt, requestHash);
            return await _emergencies.ReadAsync(actor, receipt.ResourceId, cancellationToken);
        }

        // Returns the receipt of the request that committed instead when this one lost (same key first, or a conflict
        // while the same request is the one that committed); any other conflict is rethrown (409).
        private async Task<IdempotencyReceipt?> SaveAsync(IdempotencyKey key, CancellationToken cancellationToken)
        {
            try
            {
                await _writes.SaveChangesAsync(cancellationToken);
                return null;
            }
            catch (IdempotencyKeyCollisionException)
            {
                return await _receipts.FindAsync(key, cancellationToken)
                    ?? throw new InvalidOperationException("The idempotency receipt that won the race was not found.");
            }
            catch (ResourceConflictException)
            {
                if (await _receipts.FindAsync(key, cancellationToken) is { } winner)
                {
                    return winner;
                }

                throw;
            }
        }
    }
}
