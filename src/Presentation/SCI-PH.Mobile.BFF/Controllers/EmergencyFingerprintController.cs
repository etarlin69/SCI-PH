using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;

namespace SCI_PH.Mobile.BFF.Controllers
{
    // Identification of the NN patient of an emergency by fingerprint (ADR-009): the paramedic opens a session with a
    // reader, follows it until the reader reports, and confirms the candidate. Emergencies.Operate opens the module and
    // the identification needs Patients.Read and Patients.Identify, as the identification by document. Access to the
    // emergency is the paramedic's assignment, and only before the hospital received it (EmergencyFingerprintService).
    [ApiController]
    [Route("api/mobile/emergencies/{id:guid}/identify/fingerprint/sessions")]
    [Authorize(Policy = Permissions.Emergencies.Operate)]
    [Authorize(Policy = Permissions.Patients.Read)]
    [Authorize(Policy = Permissions.Patients.Identify)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class EmergencyFingerprintController : ControllerBase
    {
        private readonly EmergencyFingerprintService _fingerprints;

        public EmergencyFingerprintController(EmergencyFingerprintService fingerprints) => _fingerprints = fingerprints;

        // 201 the first time; 200 with the same session when the same clientRequestId is retried with the same data; 404
        // for an unknown, revoked or non-fingerprint reader; 409 when the emergency is already identified, has an active
        // session, the reader is busy, the emergency was handed off, or that clientRequestId was used with different data.
        [HttpPost]
        [ProducesResponseType<FingerprintSessionSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType<FingerprintSessionSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Open(Guid id, OpenFingerprintSessionRequest request, CancellationToken cancellationToken)
        {
            var result = await _fingerprints.OpenSessionAsync(id, request, cancellationToken);
            return result.Created
                ? CreatedAtAction(nameof(Get), new { id, sessionId = result.Session.Id }, result.Session)
                : Ok(result.Session);
        }

        // The state of a session opened by the current paramedic (404 for any other).
        [HttpGet("{sessionId:guid}")]
        [ProducesResponseType<FingerprintSessionSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(Guid id, Guid sessionId, CancellationToken cancellationToken) =>
            Ok(await _fingerprints.GetSessionAsync(id, sessionId, cancellationToken));

        // Identifies the emergency with the candidate of a Matched session. 200 with the identified emergency, also when the
        // same clientRequestId is retried; 409 when the session is not Matched or expired, the reference was revoked, the
        // emergency is already identified or was handed off, or that clientRequestId was used with different data.
        [HttpPost("{sessionId:guid}/confirm")]
        [ProducesResponseType<PrehospitalEmergencySummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Confirm(
            Guid id, Guid sessionId, ConfirmFingerprintSessionRequest request, CancellationToken cancellationToken) =>
            Ok(await _fingerprints.ConfirmAsync(id, sessionId, request, cancellationToken));

        // Pending or Matched -> Cancelled. 200 also when the session already ended without an identification (a retry);
        // 409 for a confirmed one.
        [HttpPost("{sessionId:guid}/cancel")]
        [ProducesResponseType<FingerprintSessionSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Cancel(Guid id, Guid sessionId, CancellationToken cancellationToken) =>
            Ok(await _fingerprints.CancelSessionAsync(id, sessionId, cancellationToken));
    }
}
