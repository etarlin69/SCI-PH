using System.Text.Json;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Features.Clinical
{
    // Revocation of a patient's fingerprint from the hospital Web (ADR-011). Security first: in ONE transaction the reference
    // becomes Revoked (PatientBiometricReference.Revoke, Status concurrency token), its audit entry is written and a deletion
    // job is queued for its reader; from that commit the reference identifies nobody, whatever happens to the reader. The
    // physical deletion is cleanup (FingerprintSlotDeletionReaderService). Same actor rule as enrollment: Biometrics.Manage,
    // active clinical staff (no role bypass) who can see the patient.
    public sealed class FingerprintRevocationService
    {
        public const string DeletionEntityName = "FingerprintSlotDeletionJob";

        private readonly ICurrentClinicalActor _actor;
        private readonly IPatientAccessAuthorizer _patientAccess;
        private readonly IClinicalReadStore _reads;
        private readonly IClinicalWriteStore _writes;
        private readonly IFingerprintSlotDeletionStore _deletions;
        private readonly IDeviceCredentialStore _readers;
        private readonly TimeProvider _time;

        public FingerprintRevocationService(
            ICurrentClinicalActor actor,
            IPatientAccessAuthorizer patientAccess,
            IClinicalReadStore reads,
            IClinicalWriteStore writes,
            IFingerprintSlotDeletionStore deletions,
            IDeviceCredentialStore readers,
            TimeProvider time)
        {
            _actor = actor;
            _patientAccess = patientAccess;
            _reads = reads;
            _writes = writes;
            _deletions = deletions;
            _readers = readers;
            _time = time;
        }

        // Active -> Revoked at the server's now, with its Update audit entry (reason only there), and, when the reference
        // names a reader and slot of this prototype, ONE Pending deletion job with its Create audit entry. A reference that
        // cannot be parsed is still revoked (security first) but nothing is sent to a reader (cleanup AttentionRequired).
        // Already Revoked: 200 with its current state and nothing written; never a repair job, since its slot may already
        // hold another patient's template. Another patient's reference, or one that is not a fingerprint: 404. A
        // concurrent revocation wins on the Status token; this one then answers with the state it left.
        public async Task<PatientFingerprintSummary> RevokeAsync(
            Guid patientId, Guid referenceId, RevokeFingerprintRequest? request, CancellationToken cancellationToken)
        {
            var reason = request?.Reason;
            if (reason is { } given && !Enum.IsDefined(given))
            {
                throw new RequestValidationException("reason is not valid.", "reason");
            }

            var actor = await _actor.GetAsync(cancellationToken);
            await PatientFingerprints.EnsureCanManageAsync(_reads, _patientAccess, patientId, cancellationToken);
            for (var attempt = 1; ; attempt++)
            {
                var reference = await _writes.FindBiometricReferenceAsync(referenceId, cancellationToken);
                if (reference is null || reference.PatientId != patientId || reference.Modality != BiometricModality.Fingerprint)
                {
                    throw new ResourceNotFoundException(nameof(PatientBiometricReference), referenceId);
                }

                if (reference.Status == BiometricReferenceStatus.Revoked)
                {
                    return await SummaryAsync(patientId, referenceId, cancellationToken);
                }

                var now = _time.GetUtcNow();
                reference.Revoke(now);
                _writes.Add(ClinicalAuditEntry.Success(
                    actor.AuditActor, AuditActionType.Update, nameof(PatientBiometricReference), reference.Id.ToString(), now,
                    new AuditContext(PatientId: patientId, ChangesJson: JsonSerializer.Serialize(new
                    {
                        status = new[] { BiometricReferenceStatus.Active.ToString(), reference.Status.ToString() },
                        reason = reason?.ToString()
                    }))));
                if (PatientFingerprints.HasPhysicalSlot(reference.Provider, reference.SubjectReference, out var readerId))
                {
                    var deletion = FingerprintSlotDeletion.Request(reference.Id, readerId, now);
                    _deletions.Add(deletion);
                    _writes.Add(ClinicalAuditEntry.Success(
                        actor.AuditActor, AuditActionType.Create, DeletionEntityName, deletion.Id.ToString(), now,
                        new AuditContext(PatientId: patientId, ChangesJson: JsonSerializer.Serialize(new
                        {
                            biometricReferenceId = reference.Id,
                            readerId
                        }))));
                }

                try
                {
                    await _writes.SaveChangesAsync(cancellationToken);
                    return await SummaryAsync(patientId, referenceId, cancellationToken);
                }
                catch (ResourceConflictException) when (attempt < 2)
                {
                    // Another revocation committed first; nothing of this one was written. Read it again: now Revoked.
                }
            }
        }

        // As stored (the same answer for the first revocation and any replay), like the patient's list of fingerprints.
        private async Task<PatientFingerprintSummary> SummaryAsync(Guid patientId, Guid referenceId, CancellationToken cancellationToken)
        {
            var row = (await _reads.ListFingerprintReferencesAsync(patientId, cancellationToken)).Single(r => r.Id == referenceId);
            return (await PatientFingerprints.SummarizeAsync([row], _readers, _deletions, cancellationToken)).Single();
        }
    }
}
