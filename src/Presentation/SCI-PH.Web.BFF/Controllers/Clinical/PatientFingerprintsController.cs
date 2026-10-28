using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.Features.Clinical;

namespace SCI_PH.Web.BFF.Controllers.Clinical
{
    // Fingerprint references of a patient and their enrollment on a reader (ADR-010). Biometrics.Manage plus
    // Patients.Read (the patient must be visible, as for GET /api/web/patients/{id}); the service also requires active
    // clinical staff. Never a slot, a subject reference or a reader's internal id of a reference.
    [ApiController]
    [Route("api/web/patients/{patientId:guid}/fingerprints")]
    [Authorize(Policy = Permissions.Biometrics.Manage)]
    [Authorize(Policy = Permissions.Patients.Read)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class PatientFingerprintsController : ControllerBase
    {
        private readonly FingerprintEnrollmentService _enrollments;

        public PatientFingerprintsController(FingerprintEnrollmentService enrollments) => _enrollments = enrollments;

        [HttpGet]
        [ProducesResponseType<IReadOnlyList<PatientFingerprintSummary>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> List(Guid patientId, CancellationToken cancellationToken) =>
            Ok(await _enrollments.ListPatientFingerprintsAsync(patientId, cancellationToken));

        // 201 the first time; 200 with the same enrollment when the same clientRequestId is retried with the same data; 404
        // for an unknown patient or a reader that cannot enroll and identify; 409 when the patient already has an active
        // fingerprint on that reader, an enrollment is pending for the patient or the reader, or that clientRequestId was
        // used with different data.
        [HttpPost("enrollments")]
        [ProducesResponseType<FingerprintEnrollmentSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType<FingerprintEnrollmentSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Open(Guid patientId, OpenFingerprintEnrollmentRequest request, CancellationToken cancellationToken)
        {
            var result = await _enrollments.OpenAsync(patientId, request, cancellationToken);
            return result.Created
                ? CreatedAtAction(nameof(Get), new { patientId, enrollmentId = result.Enrollment.Id }, result.Enrollment)
                : Ok(result.Enrollment);
        }

        [HttpGet("enrollments/{enrollmentId:guid}")]
        [ProducesResponseType<FingerprintEnrollmentSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(Guid patientId, Guid enrollmentId, CancellationToken cancellationToken) =>
            Ok(await _enrollments.GetAsync(patientId, enrollmentId, cancellationToken));

        // Pending -> Cancelled. 200 also when it already ended without a reference (a retry); 409 when it is Enrolled.
        [HttpPost("enrollments/{enrollmentId:guid}/cancel")]
        [ProducesResponseType<FingerprintEnrollmentSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Cancel(Guid patientId, Guid enrollmentId, CancellationToken cancellationToken) =>
            Ok(await _enrollments.CancelAsync(patientId, enrollmentId, cancellationToken));
    }

    // Readers the hospital can enroll fingerprints on: active, with both Device.Fingerprint.Enroll and
    // Device.Fingerprint.Identify. Only id, code, name and last contact; never keys, hashes or scopes.
    [ApiController]
    [Route("api/web/fingerprint-enrollers")]
    [Authorize(Policy = Permissions.Biometrics.Manage)]
    public sealed class FingerprintEnrollersController : ControllerBase
    {
        private readonly FingerprintEnrollmentService _enrollments;

        public FingerprintEnrollersController(FingerprintEnrollmentService enrollments) => _enrollments = enrollments;

        [HttpGet]
        [ProducesResponseType<IReadOnlyList<FingerprintReaderSummary>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> List(CancellationToken cancellationToken) =>
            Ok(await _enrollments.ListEnrollersAsync(cancellationToken));
    }
}
