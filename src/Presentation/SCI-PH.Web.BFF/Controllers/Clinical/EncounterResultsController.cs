using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;

namespace SCI_PH.Web.BFF.Controllers.Clinical
{
    // Clinical results of an encounter (EncounterResultService): request an order, record its value, correct it or cancel
    // a pending one. Reading is clinical (Encounters.Read); every change needs Results.Write. 409 when the encounter does
    // not accept records or the result is not in a status that allows the change (also when another request changed it).
    [ApiController]
    [Route("api/web/encounters/{encounterId:guid}/results")]
    public sealed class EncounterResultsController : ControllerBase
    {
        private readonly EncounterResultService _results;

        public EncounterResultsController(EncounterResultService results) => _results = results;

        [HttpGet]
        [Authorize(Policy = Permissions.Encounters.Read)]
        [ProducesResponseType<IReadOnlyList<ClinicalResultSummary>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> List(Guid encounterId, CancellationToken cancellationToken) =>
            Ok(await _results.ListAsync(encounterId, cancellationToken));

        [HttpPost]
        [Authorize(Policy = Permissions.Results.Write)]
        [ProducesResponseType<ClinicalResultSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RequestResult(Guid encounterId, RequestClinicalResultRequest request, CancellationToken cancellationToken)
        {
            var result = await _results.RequestAsync(encounterId, request, cancellationToken);
            return CreatedAtAction(nameof(List), new { encounterId }, result);
        }

        [HttpPost("{resultId:guid}/final")]
        [Authorize(Policy = Permissions.Results.Write)]
        [ProducesResponseType<ClinicalResultSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RecordFinal(Guid encounterId, Guid resultId, RecordClinicalResultRequest request, CancellationToken cancellationToken) =>
            Ok(await _results.RecordFinalAsync(encounterId, resultId, request, cancellationToken));

        [HttpPost("{resultId:guid}/correct")]
        [Authorize(Policy = Permissions.Results.Write)]
        [ProducesResponseType<ClinicalResultSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Correct(Guid encounterId, Guid resultId, RecordClinicalResultRequest request, CancellationToken cancellationToken) =>
            Ok(await _results.CorrectAsync(encounterId, resultId, request, cancellationToken));

        [HttpPost("{resultId:guid}/cancel")]
        [Authorize(Policy = Permissions.Results.Write)]
        [ProducesResponseType<ClinicalResultSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Cancel(Guid encounterId, Guid resultId, CancellationToken cancellationToken) =>
            Ok(await _results.CancelAsync(encounterId, resultId, cancellationToken));
    }
}
