using System.Text.Json.Serialization;
using SCI_PH.Application.Common.Serialization;
using SCI_PH.Domain.Enums.Documents;

namespace SCI_PH.Application.DTOs.Documents
{
    // A document of an encounter as staff see it. Never the blob path, the checksum or the uploader's account id.
    public sealed record ClinicalDocumentSummary(
        Guid Id,
        Guid EncounterId,
        [property: JsonConverter(typeof(TextEnumConverter))] ClinicalDocumentType Type,
        string FileName,
        string ContentType,
        long SizeBytes,
        DateTimeOffset? UploadedAtUtc);

    // What the BFF hands over from the multipart request: the content stays a stream until the service reads it (bounded).
    // DeclaredContentType is only checked against the real content; the file name is only a display name.
    public sealed record UploadClinicalDocumentRequest(
        Guid ClientRequestId,
        ClinicalDocumentType Type,
        string? FileName,
        string? DeclaredContentType,
        long? Length,
        Stream Content);

    // Created = false: an idempotent replay of an upload this user already made with the same request.
    public sealed record UploadClinicalDocumentResult(ClinicalDocumentSummary Document, bool Created);

    // The content to send back, with the metadata the response needs. The caller disposes the stream.
    public sealed record ClinicalDocumentContent(Stream Content, string ContentType, string FileName);
}
