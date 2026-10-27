using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.Features.Clinical;

namespace SCI_PH.Mobile.BFF.Controllers
{
    // Readers a paramedic can identify a patient with. Not the device administration (Devices.Manage, Web BFF): only
    // active readers with the fingerprint scope, and only their id, code, name and last contact; never keys or hashes.
    [ApiController]
    [Route("api/mobile/devices")]
    [Authorize(Policy = Permissions.Emergencies.Operate)]
    [Authorize(Policy = Permissions.Patients.Read)]
    [Authorize(Policy = Permissions.Patients.Identify)]
    public sealed class DevicesController : ControllerBase
    {
        private readonly EmergencyFingerprintService _fingerprints;

        public DevicesController(EmergencyFingerprintService fingerprints) => _fingerprints = fingerprints;

        [HttpGet("fingerprint-readers")]
        [ProducesResponseType<IReadOnlyList<FingerprintReaderSummary>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> FingerprintReaders(CancellationToken cancellationToken) =>
            Ok(await _fingerprints.ListReadersAsync(cancellationToken));
    }
}
