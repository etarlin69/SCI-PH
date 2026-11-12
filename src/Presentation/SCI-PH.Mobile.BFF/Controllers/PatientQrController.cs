using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Infrastructure.Web;

namespace SCI_PH.Mobile.BFF.Controllers
{
    // The signed-in patient's own QR credential (ADR-015). No route takes a patient id. The QR identifies the clinical
    // patient to authorized staff; it never authenticates anyone. The app draws it from the opaque payload, once: the
    // server keeps only its hash and no image.
    [ApiController]
    [Route("api/mobile/patient/me/qr")]
    [Authorize(Policy = AccessPolicies.PatientSelfService)]
    [EnableRateLimiting(RateLimitPolicies.PatientQr)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class PatientQrController : ControllerBase
    {
        private readonly PatientQrService _qr;

        public PatientQrController(PatientQrService qr) => _qr = qr;

        // A new credential that replaces the current one. 403 when the account is no longer active or has no clinical
        // link; 409 when a concurrent request issued one first (nothing was issued here: try again).
        [HttpPost]
        [ProducesResponseType<PatientQrCredential>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Issue(CancellationToken cancellationToken) =>
            Ok(await _qr.IssueAsync(cancellationToken));

        // Idempotent: 204 also when there is no active credential.
        [HttpDelete]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Revoke(CancellationToken cancellationToken)
        {
            await _qr.RevokeAsync(cancellationToken);
            return NoContent();
        }
    }
}
