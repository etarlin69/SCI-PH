using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Abstractions.Documents;
using SCI_PH.Application.Abstractions.Idempotency;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Abstractions.Storage;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Idempotency;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Documents;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Constants;
using SCI_PH.Domain.Entities.Auditing;
using SCI_PH.Domain.Entities.Documents;
using SCI_PH.Domain.Entities.Encounters;
using SCI_PH.Domain.Enums.Auditing;
using SCI_PH.Domain.Enums.Documents;
using SCI_PH.Domain.ValueObjects;

namespace SCI_PH.Application.Features.Documents
{
    // Clinical documents of an encounter (the Document Service of the approved storage design). Staff of the clinic that holds
    // the encounter attach a file (Documents.Upload) and read the encounter's documents (Documents.Read), with the same clinic
    // and patient rules as every other record of the encounter.
    //
    // Upload: authorize -> validate the REAL content (PDF, PNG or JPEG by its signature, never the extension or the declared
    // type alone; at most MaxSizeBytes) -> the Storage Service keeps it at a path the server generates -> the metadata
    // (Available, with its SHA-256), the audit entry and the idempotency receipt are written in one transaction. If that save
    // fails, the stored object is removed (best effort). The client's file name is only a sanitized display name.
    //
    // Download: authorize -> metadata of an Available document of THAT encounter -> its blob path -> the Storage Service. Every
    // download is audited first (fails closed: no audit entry, no content). A document whose content is missing is a 404.
    public sealed class ClinicalDocumentService : IDocumentService
    {
        public const string UploadOperation = "encounter-document.upload.v1";

        public const long MaxSizeBytes = 10 * 1024 * 1024;

        public const string Pdf = "application/pdf";
        public const string Png = "image/png";
        public const string Jpeg = "image/jpeg";

        private const string Unavailable = "Documents are not available right now. Try again later.";

        private readonly ICurrentClinicalActor _actor;
        private readonly ICurrentUser _currentUser;
        private readonly IClinicalReadStore _reads;
        private readonly IClinicalWriteStore _writes;
        private readonly IClinicalDocumentStore _documents;
        private readonly IIdempotencyStore _receipts;
        private readonly IPatientAccessAuthorizer _patientAccess;
        private readonly IStorageService _storage;
        private readonly TimeProvider _time;
        private readonly ILogger<ClinicalDocumentService> _logger;

        public ClinicalDocumentService(
            ICurrentClinicalActor actor,
            ICurrentUser currentUser,
            IClinicalReadStore reads,
            IClinicalWriteStore writes,
            IClinicalDocumentStore documents,
            IIdempotencyStore receipts,
            IPatientAccessAuthorizer patientAccess,
            IStorageService storage,
            TimeProvider time,
            ILogger<ClinicalDocumentService> logger)
        {
            _actor = actor;
            _currentUser = currentUser;
            _reads = reads;
            _writes = writes;
            _documents = documents;
            _receipts = receipts;
            _patientAccess = patientAccess;
            _storage = storage;
            _time = time;
            _logger = logger;
        }

        public async Task<IReadOnlyList<ClinicalDocumentSummary>> ListForEncounterAsync(Guid encounterId, CancellationToken cancellationToken)
        {
            RequirePermission(Permissions.Documents.Read);
            await EncounterService.GetReadableAsync(_actor, _reads, _patientAccess, encounterId, cancellationToken);
            return await _documents.ListAvailableForEncounterAsync(encounterId, cancellationToken);
        }

        public async Task<UploadClinicalDocumentResult> UploadToEncounterAsync(
            Guid encounterId, UploadClinicalDocumentRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequirePermission(Permissions.Documents.Upload);
            // A document of an encounter is part of its clinical record: only who can read the encounter attaches one (not
            // reception, whose Documents.Upload is for administrative documents).
            RequirePermission(Permissions.Encounters.Read);
            var clientRequestId = ClinicalInput.Id(request.ClientRequestId, "clientRequestId");
            if (!Enum.IsDefined(request.Type))
            {
                throw new RequestValidationException("The document type is not valid.", "type");
            }

            var actor = await _actor.GetAsync(cancellationToken);
            var readable = await EncounterService.GetReadableAsync(_actor, _reads, _patientAccess, encounterId, cancellationToken);
            if (!EncounterRecordService.RecordableStatuses.Contains(readable.Status))
            {
                throw new ResourceConflictException($"An encounter in status {readable.Status} does not accept documents.");
            }

            var content = await ReadBoundedAsync(request.Content, request.Length, cancellationToken);
            var contentType = DetectContentType(content.Span)
                ?? throw new RequestValidationException("Only PDF, PNG and JPEG files are accepted.", "file");
            if (request.DeclaredContentType is { } declared
                && !string.Equals(Normalize(declared), contentType, StringComparison.Ordinal))
            {
                throw new RequestValidationException("The file content does not match its declared type.", "file");
            }

            var fileName = SanitizeFileName(request.FileName, contentType);
            var checksum = Convert.ToHexStringLower(SHA256.HashData(content.Span));
            var key = new IdempotencyKey(actor.IdentityUserId, UploadOperation, clientRequestId);
            var requestHash = IdempotencyRequestHash.Compute(encounterId.ToString("D"), request.Type.ToString(), checksum, fileName);
            if (await _receipts.FindAsync(key, cancellationToken) is { } existing)
            {
                return await ReplayAsync(encounterId, existing, requestHash, cancellationToken);
            }

            var encounter = await _writes.FindEncounterAsync(encounterId, cancellationToken)
                ?? throw new ResourceNotFoundException(nameof(ClinicalEncounter), encounterId);
            // Generated here, from ids only: nothing of the client (file name, type) ever reaches the storage path.
            var blobPath = $"encounters/{encounterId:N}/{Guid.NewGuid():N}";
            try
            {
                await _storage.UploadAsync(blobPath, content, contentType, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError("Document not stored: the storage failed with {ExceptionType}.", exception.GetType().Name);
                throw new ExternalServiceUnavailableException(Unavailable);
            }

            var now = _time.GetUtcNow();
            var document = ClinicalDocument.RegisterForEncounter(
                encounter.Id, request.Type, fileName, blobPath, contentType, content.Length, actor.IdentityUserId);
            document.MarkAvailable(Sha256Checksum.Parse(checksum), now);
            _documents.Add(document);
            _writes.EnsureEncounterUnchanged(encounter);
            _writes.Add(ClinicalAuditEntry.Success(
                actor.AuditActor, AuditActionType.Create, nameof(ClinicalDocument), document.Id.ToString(), now,
                new AuditContext(encounter.PatientId, encounter.Id)));
            _receipts.Add(new IdempotencyReceipt(key, requestHash, document.Id, now));
            try
            {
                await _writes.SaveChangesAsync(cancellationToken);
            }
            catch (IdempotencyKeyCollisionException)
            {
                // The same request committed first: this copy of the content is not referenced by anything.
                await DeleteQuietlyAsync(blobPath);
                var winner = await _receipts.FindAsync(key, cancellationToken)
                    ?? throw new InvalidOperationException("The idempotency receipt that won the race was not found.");
                return await ReplayAsync(encounterId, winner, requestHash, cancellationToken);
            }
            catch
            {
                await DeleteQuietlyAsync(blobPath);
                throw;
            }

            return new UploadClinicalDocumentResult(Summary(document), Created: true);
        }

        public async Task<ClinicalDocumentContent> OpenFromEncounterAsync(Guid encounterId, Guid documentId, CancellationToken cancellationToken)
        {
            RequirePermission(Permissions.Documents.Read);
            var actor = await _actor.GetAsync(cancellationToken);
            var encounter = await EncounterService.GetReadableAsync(_actor, _reads, _patientAccess, encounterId, cancellationToken);
            var document = await _documents.FindForEncounterAsync(encounterId, documentId, cancellationToken);
            if (document is not { Status: ClinicalDocumentStatus.Available })
            {
                throw new ResourceNotFoundException(nameof(ClinicalDocument), documentId);
            }

            Stream? content;
            try
            {
                content = await _storage.OpenReadAsync(document.BlobPath, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError("Document content not read: the storage failed with {ExceptionType}.", exception.GetType().Name);
                throw new ExternalServiceUnavailableException(Unavailable);
            }

            if (content is null)
            {
                // Metadata without its object: an inconsistency to look at, never a server error for the user.
                _logger.LogWarning("Document {DocumentId} has no stored content.", document.Id);
                throw new ResourceNotFoundException(nameof(ClinicalDocument), documentId);
            }

            try
            {
                _writes.Add(ClinicalAuditEntry.Success(
                    actor.AuditActor, AuditActionType.Read, nameof(ClinicalDocument), document.Id.ToString(), _time.GetUtcNow(),
                    new AuditContext(encounter.PatientId, encounter.Id)));
                await _writes.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await content.DisposeAsync();
                _logger.LogError("Document content not returned: its audit failed with {ExceptionType}.", exception.GetType().Name);
                throw new ExternalServiceUnavailableException(Unavailable);
            }

            return new ClinicalDocumentContent(content, document.ContentType, document.FileName);
        }

        // The magic number decides the type: an executable renamed to .pdf, or a PDF declared as an image, is rejected.
        public static string? DetectContentType(ReadOnlySpan<byte> content) => content switch
        {
            [0x25, 0x50, 0x44, 0x46, 0x2D, ..] => Pdf, // %PDF-
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => Png,
            [0xFF, 0xD8, 0xFF, ..] => Jpeg,
            _ => null
        };

        // Only a display name: the last path segment, without control or reserved characters, bounded, with the extension of
        // its real type. Never used for the storage path.
        public static string SanitizeFileName(string? fileName, string contentType)
        {
            var name = (fileName ?? string.Empty).Replace('\\', '/');
            name = name[(name.LastIndexOf('/') + 1)..];
            var clean = new StringBuilder(name.Length);
            foreach (var c in name.Normalize(NormalizationForm.FormC))
            {
                if (!char.IsControl(c) && c is not ('<' or '>' or ':' or '"' or '|' or '?' or '*'))
                {
                    clean.Append(c);
                }
            }

            var stem = Path.GetFileNameWithoutExtension(clean.ToString()).Trim().Trim('.').Trim();
            if (stem.Length == 0)
            {
                stem = "documento";
            }

            var extension = contentType switch { Pdf => ".pdf", Png => ".png", _ => ".jpg" };
            var maxStem = FieldLengths.FileName - extension.Length;
            return (stem.Length > maxStem ? stem[..maxStem] : stem) + extension;
        }

        private static async Task<ReadOnlyMemory<byte>> ReadBoundedAsync(Stream content, long? length, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(content);
            if (length is > MaxSizeBytes)
            {
                throw TooLarge();
            }

            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxSizeBytes)
                {
                    throw TooLarge();
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.Length == 0
                ? throw new RequestValidationException("The file is empty.", "file")
                : buffer.ToArray();
        }

        private static RequestValidationException TooLarge() =>
            new($"The file is larger than {MaxSizeBytes / (1024 * 1024)} MB.", "file");

        private static string Normalize(string contentType)
        {
            var value = contentType.Split(';')[0].Trim().ToLowerInvariant();
            return value == "image/jpg" ? Jpeg : value;
        }

        private async Task<UploadClinicalDocumentResult> ReplayAsync(
            Guid encounterId, IdempotencyReceipt receipt, string requestHash, CancellationToken cancellationToken)
        {
            PrehospitalEmergencyService.EnsureSameRequest(receipt, requestHash);
            var document = await _documents.FindForEncounterAsync(encounterId, receipt.ResourceId, cancellationToken)
                ?? throw new ResourceConflictException("clientRequestId was already used for another encounter.");
            return new UploadClinicalDocumentResult(Summary(document), Created: false);
        }

        private async Task DeleteQuietlyAsync(string blobPath)
        {
            try
            {
                await _storage.DeleteAsync(blobPath, CancellationToken.None);
            }
            catch (Exception exception)
            {
                _logger.LogWarning("Unreferenced document content not removed: {ExceptionType}.", exception.GetType().Name);
            }
        }

        private void RequirePermission(string permission)
        {
            if (!_currentUser.HasPermission(permission))
            {
                throw new AccessDeniedException($"This operation requires the {permission} permission.");
            }
        }

        private static ClinicalDocumentSummary Summary(ClinicalDocument document) => new(
            document.Id, document.ClinicalEncounterId!.Value, document.Type, document.FileName, document.ContentType, document.SizeBytes,
            document.UploadedAtUtc);
    }
}
