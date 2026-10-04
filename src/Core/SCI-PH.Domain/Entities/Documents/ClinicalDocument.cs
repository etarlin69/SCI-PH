using SCI_PH.Domain.Common;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Entities.Patients;
using SCI_PH.Domain.Enums.Documents;
using SCI_PH.Domain.Exceptions;
using SCI_PH.Domain.ValueObjects;

namespace SCI_PH.Domain.Entities.Documents
{
    // Metadata and object-storage path only; the file content never goes into PostgreSQL.
    // A document belongs to exactly one owner: the patient record, or an encounter (patient derived from it).
    // This avoids two patient references that could contradict each other and lets NN documents follow
    // the encounter when it is identified.
    public sealed class ClinicalDocument
    {
        private ClinicalDocument() { }

        public Guid Id { get; private set; }
        public Guid? PatientId { get; private set; }
        public Patient? Patient { get; private set; }
        public Guid? ClinicalEncounterId { get; private set; }
        public ClinicalEncounter? Encounter { get; private set; }
        public ClinicalDocumentType Type { get; private set; }
        public ClinicalDocumentStatus Status { get; private set; }
        public string FileName { get; private set; } = string.Empty;
        public string BlobPath { get; private set; } = string.Empty;
        public string ContentType { get; private set; } = string.Empty;
        public long SizeBytes { get; private set; }
        public Sha256Checksum? ChecksumSha256 { get; private set; }
        // Account that uploaded the file (staff or patient app); no cross-context FK.
        public Guid? UploadedByIdentityUserId { get; private set; }
        public DateTimeOffset CreatedAtUtc { get; private set; }
        public DateTimeOffset? UploadedAtUtc { get; private set; }
        public DateTimeOffset? DeletedAtUtc { get; private set; }

        public static ClinicalDocument RegisterForPatient(
            Guid patientId,
            ClinicalDocumentType type,
            string fileName,
            string blobPath,
            string contentType,
            long sizeBytes,
            Guid? uploadedByIdentityUserId = null)
        {
            var document = Create(type, fileName, blobPath, contentType, sizeBytes, uploadedByIdentityUserId);
            document.PatientId = Guard.Id(patientId, nameof(patientId));
            return document;
        }

        public static ClinicalDocument RegisterForEncounter(
            Guid clinicalEncounterId,
            ClinicalDocumentType type,
            string fileName,
            string blobPath,
            string contentType,
            long sizeBytes,
            Guid? uploadedByIdentityUserId = null)
        {
            var document = Create(type, fileName, blobPath, contentType, sizeBytes, uploadedByIdentityUserId);
            document.ClinicalEncounterId = Guard.Id(clinicalEncounterId, nameof(clinicalEncounterId));
            return document;
        }

        // The storage service calls this after verifying the uploaded object and computing its hash.
        public void MarkAvailable(Sha256Checksum checksum, DateTimeOffset uploadedAtUtc)
        {
            ArgumentNullException.ThrowIfNull(checksum);
            EnsureStatus(ClinicalDocumentStatus.PendingUpload);
            ChecksumSha256 = checksum;
            UploadedAtUtc = Guard.Utc(uploadedAtUtc);
            Status = ClinicalDocumentStatus.Available;
        }

        public void Reject()
        {
            EnsureStatus(ClinicalDocumentStatus.PendingUpload);
            Status = ClinicalDocumentStatus.Rejected;
        }

        // Logical deletion keeps the metadata row; removing the stored object is the storage service's job.
        public void MarkDeleted(DateTimeOffset deletedAtUtc)
        {
            if (Status == ClinicalDocumentStatus.Deleted)
            {
                throw new DomainException("The document is already deleted.");
            }

            DeletedAtUtc = Guard.Utc(deletedAtUtc);
            Status = ClinicalDocumentStatus.Deleted;
        }

        private static ClinicalDocument Create(
            ClinicalDocumentType type,
            string fileName,
            string blobPath,
            string contentType,
            long sizeBytes,
            Guid? uploadedByIdentityUserId) =>
            new()
            {
                Id = Guid.NewGuid(),
                Type = Guard.Defined(type, nameof(type)),
                FileName = Guard.Text(fileName, FieldLengths.FileName, nameof(fileName)),
                BlobPath = Guard.Text(blobPath, FieldLengths.BlobPath, nameof(blobPath)),
                ContentType = Guard.Text(contentType, FieldLengths.ContentType, nameof(contentType)).ToLowerInvariant(),
                SizeBytes = sizeBytes > 0 ? sizeBytes : throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "A document cannot be empty."),
                UploadedByIdentityUserId = Guard.OptionalId(uploadedByIdentityUserId, nameof(uploadedByIdentityUserId)),
                Status = ClinicalDocumentStatus.PendingUpload,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

        private void EnsureStatus(ClinicalDocumentStatus expected)
        {
            if (Status != expected)
            {
                throw new DomainException($"The document must be {expected}; current status is {Status}.");
            }
        }
    }
}
