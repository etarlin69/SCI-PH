using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Infrastructure.Web;

namespace SCI_PH.Mobile.BFF.Controllers
{
    // Identification of the NN patient of an emergency by document: resolve a candidate, then the staff member confirms it
    // (EmergencyDocumentIdentificationService). The same permissions as the identification by QR or fingerprint; access to
    // the emergency is the paramedic's assignment, and only before the hospital received it. The document always travels in
    // the body, never in the route. Both steps share a per-user rate limit: a document number can be guessed.
    [ApiController]
    [Route("api/mobile/emergencies/{id:guid}/identify/document")]
    [Authorize(Policy = Permissions.Emergencies.Operate)]
    [Authorize(Policy = Permissions.Patients.Read)]
    [Authorize(Policy = Permissions.Patients.Identify)]
    [EnableRateLimiting(RateLimitPolicies.PatientDocumentResolve)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class EmergencyDocumentController : ControllerBase
    {
        private readonly EmergencyDocumentIdentificationService _documents;

        public EmergencyDocumentController(EmergencyDocumentIdentificationService documents) => _documents = documents;

        // The candidate for a human confirmation (name, date of birth, masked document); the emergency stays NN. 404 when no
        // patient holds that document; 409 when the emergency is already identified or was handed off; 503 and no candidate
        // if the read cannot be audited.
        [HttpPost("resolve")]
        [ProducesResponseType<PatientDocumentCandidate>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Resolve(Guid id, ResolveEmergencyDocumentRequest request, CancellationToken cancellationToken) =>
            Ok(await _documents.ResolveAsync(id, request, cancellationToken));

        // The staff member confirmed the candidate: the server resolves the document again and identifies the SAME emergency.
        // Also 200 when the same clientRequestId is retried with the same document; 404 when no patient holds it any more;
        // 409 when it is already identified, was handed off or that clientRequestId was used with different data.
        [HttpPost]
        [ProducesResponseType<PrehospitalEmergencySummary>(StatusCodes.Status200OK)]
        public async Task<IActionResult> Confirm(Guid id, IdentifyEmergencyByDocumentRequest request, CancellationToken cancellationToken) =>
            Ok(await _documents.ConfirmAsync(id, request, cancellationToken));
    }
}
