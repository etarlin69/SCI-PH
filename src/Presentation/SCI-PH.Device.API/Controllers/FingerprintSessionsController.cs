using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.Features.Devices;
using SCI_PH.Infrastructure.Web;

namespace SCI_PH.Device.API.Controllers
{
    // The fingerprint reader polls for its capture job and reports what the sensor matched (ADR-009). The reader is the
    // authenticated device (Device.Fingerprint.Identify); it never receives patient or clinical data: the pending session
    // is only an id and an expiry, and a result is answered with 204 or a generic error.
    [ApiController]
    [Route("api/device/fingerprint/sessions")]
    [Authorize(Policy = DeviceScopes.FingerprintIdentify)]
    [EnableRateLimiting(RateLimitPolicies.Device)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class FingerprintSessionsController : ControllerBase
    {
        private readonly FingerprintReaderService _readers;

        public FingerprintSessionsController(FingerprintReaderService readers) => _readers = readers;

        private Guid ReaderId => Guid.Parse(User.FindFirst(CustomClaimTypes.DeviceId)!.Value);

        private string ReaderCode => User.FindFirst(CustomClaimTypes.DeviceCode)!.Value;

        // 200 with the Pending, unexpired session of this reader; 204 when there is none.
        [HttpGet("pending")]
        [ProducesResponseType<PendingFingerprintSession>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Pending(CancellationToken cancellationToken) =>
            await _readers.GetPendingAsync(ReaderId, cancellationToken) is { } session ? Ok(session) : NoContent();

        // 204 when the result is accepted, also when the same result is sent again; 400 invalid body; 404 a session of
        // another reader or none; 409 another result for the session, or an expired or cancelled session.
        [HttpPost("{sessionId:guid}/result")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Result(Guid sessionId, SubmitFingerprintResultRequest request, CancellationToken cancellationToken)
        {
            await _readers.SubmitResultAsync(ReaderId, ReaderCode, sessionId, request, cancellationToken);
            return NoContent();
        }
    }
}
