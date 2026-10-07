using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;

namespace SCI_PH.Web.BFF.Controllers.Clinical
{
    // Reception surface for locating encounters (including NN ones) to identify the patient. Patients.Identify is
    // the existing permission for that task; the response is AdmissionEncounterSummary, which has no clinical content.
    [ApiController]
    [Route("api/web/admissions/encounters")]
    [Authorize(Policy = Permissions.Patients.Identify)]
    public sealed class AdmissionsController : ControllerBase
    {
        private readonly AdmissionService _admissions;

        public AdmissionsController(AdmissionService admissions) => _admissions = admissions;

        [HttpGet]
        [ProducesResponseType<IReadOnlyList<AdmissionEncounterSummary>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> List(Guid? clinicId, bool unidentified = false, CancellationToken cancellationToken = default) =>
            Ok(await _admissions.ListAsync(clinicId, unidentified, cancellationToken));

        [HttpGet("{id:guid}")]
        [ProducesResponseType<AdmissionEncounterSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
            Ok(await _admissions.GetAsync(id, cancellationToken));
    }
}
