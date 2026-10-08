using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;

namespace SCI_PH.Web.BFF.Controllers.Clinical
{
    // Clinics the signed-in ClinicalStaff can act in (403 when the user is not active clinical staff). A client with
    // several affiliations uses it to choose the clinicId it sends; the server still validates every clinicId.
    [ApiController]
    [Route("api/web/clinical/context")]
    public sealed class ClinicalContextController : ControllerBase
    {
        private readonly EncounterService _encounters;

        public ClinicalContextController(EncounterService encounters) => _encounters = encounters;

        [HttpGet]
        [ProducesResponseType<ClinicalContextSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
            Ok(await _encounters.GetContextAsync(cancellationToken));
    }
}
