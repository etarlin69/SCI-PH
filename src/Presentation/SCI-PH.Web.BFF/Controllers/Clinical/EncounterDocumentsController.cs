using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using SCI_PH.Application.Abstractions.Documents;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Documents;
using SCI_PH.Application.Features.Documents;
using SCI_PH.Domain.Enums.Documents;

namespace SCI_PH.Web.BFF.Controllers.Clinical
{
    // Clinical documents of an encounter. The BFF never touches the storage: it hands the multipart file to the Document
    // Service (IDocumentService), which validates, authorizes, has the Storage Service keep the content and writes the
    // metadata; a download goes metadata -> blob path -> Storage Service, all inside the service. Upload is multipart with
    // exactly `file`, `type` and `clientRequestId` (anything else is a 400), kept in memory. Responses never carry a blob path.
    [ApiController]
    [Route("api/web/encounters/{encounterId:guid}/documents")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class EncounterDocumentsController : ControllerBase
    {
        // The file limit (ClinicalDocumentService.MaxSizeBytes) is checked by the service (400); this bounds the request.
        private const int MaxRequestBytes = (int)ClinicalDocumentService.MaxSizeBytes + 64 * 1024;
        private const string FileField = "file";
        private const string TypeField = "type";
        private const string ClientRequestIdField = "clientRequestId";

        private readonly IDocumentService _documents;

        public EncounterDocumentsController(IDocumentService documents) => _documents = documents;

        [HttpGet]
        [Authorize(Policy = Permissions.Documents.Read)]
        [ProducesResponseType<IReadOnlyList<ClinicalDocumentSummary>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> List(Guid encounterId, CancellationToken cancellationToken) =>
            Ok(await _documents.ListForEncounterAsync(encounterId, cancellationToken));

        // 201 new document; 200 a retried request (same clientRequestId and file); 400 invalid form, type, size or content;
        // 409 the encounter does not accept records, or the clientRequestId was used with other data; 503 storage unavailable.
        [HttpPost]
        [Authorize(Policy = Permissions.Documents.Upload)]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes, MemoryBufferThreshold = MaxRequestBytes, ValueCountLimit = 4)]
        [ProducesResponseType<ClinicalDocumentSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType<ClinicalDocumentSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Upload(Guid encounterId, CancellationToken cancellationToken)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            if (form.Keys.Any(key => key is not (TypeField or ClientRequestIdField))
                || form.Files.Count != 1
                || form.Files[0].Name != FileField)
            {
                throw new RequestValidationException("The form must contain exactly one file, its type and the clientRequestId.", FileField);
            }

            if (form[ClientRequestIdField] is not { Count: 1 } ids || !Guid.TryParse(ids[0], out var clientRequestId))
            {
                throw new RequestValidationException("clientRequestId is required.", ClientRequestIdField);
            }

            // Names only ("LabResult"), never numbers: an undefined value cannot slip in.
            if (form[TypeField] is not { Count: 1 } types
                || types[0] is not { } typeName
                || typeName.Any(char.IsDigit)
                || !Enum.TryParse<ClinicalDocumentType>(typeName, ignoreCase: false, out var type)
                || !Enum.IsDefined(type))
            {
                throw new RequestValidationException("The document type is not valid.", TypeField);
            }

            var file = form.Files[0];
            await using var content = file.OpenReadStream();
            var result = await _documents.UploadToEncounterAsync(
                encounterId, new UploadClinicalDocumentRequest(clientRequestId, type, file.FileName, file.ContentType, file.Length, content),
                cancellationToken);
            return result.Created ? StatusCode(StatusCodes.Status201Created, result.Document) : Ok(result.Document);
        }

        // Always an attachment with the stored type (never rendered inline by the browser, never sniffed).
        [HttpGet("{documentId:guid}/content")]
        [Authorize(Policy = Permissions.Documents.Read)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Download(Guid encounterId, Guid documentId, CancellationToken cancellationToken)
        {
            var document = await _documents.OpenFromEncounterAsync(encounterId, documentId, cancellationToken);
            Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
            return File(document.Content, document.ContentType, document.FileName);
        }
    }
}
