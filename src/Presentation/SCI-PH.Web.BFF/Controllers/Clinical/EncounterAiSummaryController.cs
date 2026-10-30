using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Infrastructure.Web;

namespace SCI_PH.Web.BFF.Controllers.Clinical
{
    // Assisted encounter summary (ADR-013): AI-generated, read-only, ephemeral, requires clinician review. Same access as
    // reading the encounter. POST because each call is an audited, costly generation (never cached by intermediaries);
    // the body carries no options (empty or {}): any member is a 400. Rate limited per user (window + one at a time).
    [ApiController]
    [Route("api/web/encounters")]
    public sealed class EncounterAiSummaryController : ControllerBase
    {
        private readonly EncounterAiSummaryService _summaries;

        public EncounterAiSummaryController(EncounterAiSummaryService summaries) => _summaries = summaries;

        [HttpPost("{id:guid}/ai-summary")]
        [Authorize(Policy = Permissions.Encounters.Read)]
        [EnableRateLimiting(RateLimitPolicies.ClinicalAi)]
        [ProducesResponseType<EncounterAiSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Generate(
            Guid id,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] GenerateEncounterAiSummaryRequest? request,
            CancellationToken cancellationToken) =>
            Ok(await _summaries.GenerateAsync(id, cancellationToken));
    }
}
