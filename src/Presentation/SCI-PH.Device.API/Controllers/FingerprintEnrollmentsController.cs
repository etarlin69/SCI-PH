using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.Features.Devices;
using SCI_PH.Infrastructure.Web;

namespace SCI_PH.Device.API.Controllers
{
    // The reader polls for its enrollment job and reports the slot its sensor stored the template in (ADR-010). Requires
    // Device.Fingerprint.Enroll (Identify alone is not enough). The reader never receives the patient: the pending job is
    // only an id and an expiry, and a result is answered with 204 or a generic error. It keeps the slot only after a 204.
    [ApiController]
    [Route("api/device/fingerprint/enrollments")]
    [Authorize(Policy = DeviceScopes.FingerprintEnroll)]
    [EnableRateLimiting(RateLimitPolicies.Device)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class FingerprintEnrollmentsController : ControllerBase
    {
        private readonly FingerprintEnrollmentReaderService _enrollments;

        public FingerprintEnrollmentsController(FingerprintEnrollmentReaderService enrollments) => _enrollments = enrollments;

        private Guid ReaderId => Guid.Parse(User.FindFirst(CustomClaimTypes.DeviceId)!.Value);

        private string ReaderCode => User.FindFirst(CustomClaimTypes.DeviceCode)!.Value;

        // 200 with the Pending, unexpired enrollment of this reader; 204 when there is none.
        [HttpGet("pending")]
        [ProducesResponseType<PendingFingerprintEnrollment>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Pending(CancellationToken cancellationToken) =>
            await _enrollments.GetPendingAsync(ReaderId, cancellationToken) is { } enrollment ? Ok(enrollment) : NoContent();

        // 204 when the result is accepted, also when the same result is sent again; 400 invalid body; 404 an enrollment of
        // another reader or none; 409 the slot is already in use, another result was accepted, or the enrollment expired or
        // was cancelled (the reader then deletes the slot it just stored).
        [HttpPost("{enrollmentId:guid}/result")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Result(
            Guid enrollmentId, SubmitFingerprintEnrollmentResultRequest request, CancellationToken cancellationToken)
        {
            await _enrollments.SubmitResultAsync(ReaderId, ReaderCode, enrollmentId, request, cancellationToken);
            return NoContent();
        }
    }
}
