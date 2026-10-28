using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.Features.Clinical;

namespace SCI_PH.Web.BFF.Controllers.Clinical
{
    // Assisted 1:1 face verification of a known patient (ADR-012): staff enroll, verify and revoke the patient's face.
    // Captures are multipart/form-data with exactly two parts, `capture` (the image) and `clientRequestId`; anything else
    // (a patientId, a provider, a threshold, a reference id, a second file) is a 400. The form is kept in memory (never
    // buffered to a temporary file). Responses never carry a subject reference, template, score or threshold.
    [ApiController]
    [Route("api/web/patients/{patientId:guid}/faces")]
    [Authorize(Policy = Permissions.Patients.Read)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class PatientFacesController : ControllerBase
    {
        // The 2 MB capture limit is checked by the normalizer (400); this only bounds the whole multipart request.
        private const int MaxRequestBytes = 4 * 1024 * 1024;
        private const string CaptureField = "capture";
        private const string ClientRequestIdField = "clientRequestId";

        private readonly PatientFaceService _faces;

        public PatientFacesController(PatientFaceService faces) => _faces = faces;

        // Biometrics.Manage or Patients.Identify (checked by the service, with the clinical actor and the patient access).
        [HttpGet]
        [ProducesResponseType<IReadOnlyList<PatientFaceSummary>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> List(Guid patientId, CancellationToken cancellationToken) =>
            Ok(await _faces.ListAsync(patientId, cancellationToken));

        // 201 with the new Active reference; 200 for a retried request (same clientRequestId and capture) or a capture that
        // creates nothing (NoFace, MultipleFaces, LowQuality); 400 invalid image or form; 409 an active face already
        // exists or the clientRequestId was used with another capture; 503 the face provider is unavailable.
        [HttpPost("enroll")]
        [Authorize(Policy = Permissions.Biometrics.Manage)]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes, MemoryBufferThreshold = MaxRequestBytes, ValueCountLimit = 4)]
        [ProducesResponseType<FaceEnrollmentResponse>(StatusCodes.Status201Created)]
        [ProducesResponseType<FaceEnrollmentResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Enroll(Guid patientId, CancellationToken cancellationToken)
        {
            var upload = await ReadCaptureAsync(cancellationToken);
            await using (upload.Content)
            {
                var result = await _faces.EnrollAsync(patientId, upload, cancellationToken);
                return result.Created ? StatusCode(StatusCodes.Status201Created, result.Response) : Ok(result.Response);
            }
        }

        // 200 with Match, NoMatch, NoFace, MultipleFaces or LowQuality (also for a retried request: the recorded outcome);
        // 400 invalid image or form; 409 no active face reference; 503 the face provider is unavailable.
        [HttpPost("verify")]
        [Authorize(Policy = Permissions.Patients.Identify)]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes, MemoryBufferThreshold = MaxRequestBytes, ValueCountLimit = 4)]
        [ProducesResponseType<FaceVerificationResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Verify(Guid patientId, CancellationToken cancellationToken)
        {
            var upload = await ReadCaptureAsync(cancellationToken);
            await using (upload.Content)
            {
                return Ok(await _faces.VerifyAsync(patientId, upload, cancellationToken));
            }
        }

        // Active -> Revoked; 200 also when it was already revoked (nothing written); 400 unknown reason or other member;
        // 404 another patient's reference, one that is not a face, or none. The body is optional.
        [HttpPost("{referenceId:guid}/revoke")]
        [Authorize(Policy = Permissions.Biometrics.Manage)]
        [ProducesResponseType<PatientFaceSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Revoke(
            Guid patientId, Guid referenceId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RevokeFaceRequest? request,
            CancellationToken cancellationToken) =>
            Ok(await _faces.RevokeAsync(patientId, referenceId, request, cancellationToken));

        // Exactly one `capture` file and one `clientRequestId` value; nothing else is accepted.
        private async Task<FaceCaptureUpload> ReadCaptureAsync(CancellationToken cancellationToken)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            if (form.Keys.Any(key => key != ClientRequestIdField)
                || form.Files.Count != 1
                || form.Files[0].Name != CaptureField)
            {
                throw new RequestValidationException("The form must contain exactly one capture and the clientRequestId.", CaptureField);
            }

            if (form[ClientRequestIdField] is not { Count: 1 } values || !Guid.TryParse(values[0], out var clientRequestId))
            {
                throw new RequestValidationException("clientRequestId is required.", ClientRequestIdField);
            }

            var capture = form.Files[0];
            return new FaceCaptureUpload(clientRequestId, capture.OpenReadStream(), capture.Length);
        }
    }
}
