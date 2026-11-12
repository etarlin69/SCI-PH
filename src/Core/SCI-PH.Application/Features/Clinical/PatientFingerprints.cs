using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Biometrics;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Patients;

namespace SCI_PH.Application.Features.Clinical
{
    // What enrollment (ADR-010) and revocation (ADR-011) share about a patient's fingerprints: who may manage them and how a
    // reference is shown to the hospital Web (reader code and name, cleanup state; never a slot or a subject reference).
    public static class PatientFingerprints
    {
        // A Pending cleanup with this many failed physical attempts is shown as AttentionRequired; the reader keeps trying.
        public const int AttentionAttempts = 5;

        // The patient exists (404) and the actor may see it and manage its biometrics (403), as when reading the patient.
        public static async Task EnsureCanManageAsync(
            IClinicalReadStore reads, IPatientAccessAuthorizer patientAccess, Guid patientId, CancellationToken cancellationToken)
        {
            if (!await reads.PatientExistsAsync(patientId, cancellationToken))
            {
                throw new ResourceNotFoundException(nameof(Patient), patientId);
            }

            if (!await patientAccess.CanReadAsync(patientId, Permissions.Patients.Read, cancellationToken)
                || !await patientAccess.CanWriteAsync(patientId, Permissions.Biometrics.Manage, cancellationToken))
            {
                throw new AccessDeniedException("The current user cannot manage this patient's fingerprints.");
            }
        }

        public static async Task<IReadOnlyList<PatientFingerprintSummary>> SummarizeAsync(
            IReadOnlyList<FingerprintReferenceRow> rows,
            IDeviceCredentialStore readers,
            IFingerprintSlotDeletionStore deletions,
            CancellationToken cancellationToken)
        {
            var devices = (await readers.ListAsync(cancellationToken)).ToDictionary(d => d.Id);
            var revoked = rows.Where(r => r.Status == BiometricReferenceStatus.Revoked).Select(r => r.Id).ToList();
            var jobs = revoked.Count == 0
                ? new Dictionary<Guid, FingerprintSlotDeletion>()
                : await deletions.ListByReferencesAsync(revoked, cancellationToken);
            return rows.Select(row => new PatientFingerprintSummary(
                    row.Id,
                    row.Status,
                    FingerprintReferences.ReaderOf(row.SubjectReference) is { } readerId && devices.TryGetValue(readerId, out var reader)
                        ? new FingerprintReferenceReader(reader.DeviceCode, reader.Name, reader.Status == DeviceCredentialStatus.Active
                            && reader.Scopes.Contains(DeviceScopes.FingerprintIdentify))
                        : null,
                    row.CreatedAtUtc,
                    row.RevokedAtUtc,
                    Cleanup(row, jobs.GetValueOrDefault(row.Id), devices)))
                .ToList();
        }

        // Whether a reference names a reader and a slot this prototype can ask to delete.
        public static bool HasPhysicalSlot(string provider, string subjectReference, out Guid readerId) =>
            FingerprintReferences.TryParse(subjectReference, out readerId, out _)
            && string.Equals(provider, FingerprintReferences.Provider, StringComparison.Ordinal);

        private static FingerprintCleanupStatus? Cleanup(
            FingerprintReferenceRow row, FingerprintSlotDeletion? job, IReadOnlyDictionary<Guid, DeviceSummary> devices)
        {
            if (row.Status != BiometricReferenceStatus.Revoked)
            {
                return null;
            }

            if (job is null)
            {
                // Revoked before revocation queued cleanup (history): nothing is asked. One whose reader and slot cannot be
                // known (legacy format) was never queued either, and someone has to look at that reader.
                return HasPhysicalSlot(row.Provider, row.SubjectReference, out _)
                    ? FingerprintCleanupStatus.NotRequired
                    : FingerprintCleanupStatus.AttentionRequired;
            }

            if (!job.IsPending)
            {
                return FingerprintCleanupStatus.Completed;
            }

            return devices.TryGetValue(job.DeviceCredentialId, out var reader)
                && reader.Status == DeviceCredentialStatus.Active
                && reader.Scopes.Contains(DeviceScopes.FingerprintEnroll)
                && job.Attempts < AttentionAttempts
                    ? FingerprintCleanupStatus.Pending
                    : FingerprintCleanupStatus.AttentionRequired;
        }
    }
}
