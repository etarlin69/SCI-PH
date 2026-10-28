using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.Features.Devices;
using SCI_PH.Infrastructure.Web;

namespace SCI_PH.Device.API.Controllers
{
    // The reader polls for the slots of revoked fingerprints it must delete from its sensor and reports each physical attempt
    // (ADR-011). Requires Device.Fingerprint.Enroll: the capability that writes slots also deletes them. The pending job is
    // only an id and a slot of its own sensor; never the patient or the reference. Results are answered with 204 or a
    // generic error.
    [ApiController]
    [Route("api/device/fingerprint/deletions")]
    [Authorize(Policy = DeviceScopes.FingerprintEnroll)]
    [EnableRateLimiting(RateLimitPolicies.Device)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class FingerprintSlotDeletionsController : ControllerBase
    {
        private readonly FingerprintSlotDeletionReaderService _deletions;

        public FingerprintSlotDeletionsController(FingerprintSlotDeletionReaderService deletions) => _deletions = deletions;

        private Guid ReaderId => Guid.Parse(User.FindFirst(CustomClaimTypes.DeviceId)!.Value);

        private string ReaderCode => User.FindFirst(CustomClaimTypes.DeviceCode)!.Value;

        // 200 with the oldest Pending deletion of this reader; 204 when there is none.
        [HttpGet("pending")]
        [ProducesResponseType<PendingFingerprintSlotDeletion>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Pending(CancellationToken cancellationToken) =>
            await _deletions.GetPendingAsync(ReaderId, cancellationToken) is { } deletion ? Ok(deletion) : NoContent();

        // 204 when the attempt is accepted, also when the same attempt is sent again with the same outcome; 400 invalid body;
        // 404 a deletion of another reader or none; 409 the same attempt with another outcome, or a new attempt for a
        // deletion that is already completed.
        [HttpPost("{deletionId:guid}/result")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Result(
            Guid deletionId, SubmitFingerprintSlotDeletionResultRequest request, CancellationToken cancellationToken)
        {
            await _deletions.SubmitResultAsync(ReaderId, ReaderCode, deletionId, request, cancellationToken);
            return NoContent();
        }
    }
}
