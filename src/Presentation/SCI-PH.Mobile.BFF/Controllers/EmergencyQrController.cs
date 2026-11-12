using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Infrastructure.Web;

namespace SCI_PH.Mobile.BFF.Controllers
{
    // Identification of the NN patient of an emergency with the patient's QR (ADR-015). The same permissions as the
    // identification by document or fingerprint; access to the emergency is the paramedic's assignment, and only before the
    // hospital received it (EmergencyQrIdentificationService). The QR always travels in the body, never in the route.
    [ApiController]
    [Route("api/mobile/emergencies/{id:guid}/identify/qr")]
    [Authorize(Policy = Permissions.Emergencies.Operate)]
    [Authorize(Policy = Permissions.Patients.Read)]
    [Authorize(Policy = Permissions.Patients.Identify)]
    [EnableRateLimiting(RateLimitPolicies.PatientQrResolve)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class EmergencyQrController : ControllerBase
    {
        private readonly EmergencyQrIdentificationService _qr;

        public EmergencyQrController(EmergencyQrIdentificationService qr) => _qr = qr;

        // The candidate for a human confirmation; the emergency stays NN. 404 for any invalid QR, without saying why; 503
        // and no candidate if the read cannot be audited.
        [HttpPost("resolve")]
        [ProducesResponseType<PatientQrCandidate>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Resolve(Guid id, ResolvePatientQrRequest request, CancellationToken cancellationToken) =>
            Ok(await _qr.ResolveAsync(id, request, cancellationToken));

        // The staff member confirmed the candidate: the emergency is identified. Also 200 when the same clientRequestId is
        // retried with the same QR; 409 when it is already identified or that clientRequestId was used with different data.
        [HttpPost("confirm")]
        [ProducesResponseType<PrehospitalEmergencySummary>(StatusCodes.Status200OK)]
        public async Task<IActionResult> Confirm(Guid id, ConfirmPatientQrRequest request, CancellationToken cancellationToken) =>
            Ok(await _qr.ConfirmAsync(id, request, cancellationToken));
    }
}
