using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Enums.Organizations;

namespace SCI_PH.Web.BFF.Controllers.Admin
{
    // Clinics of the network (Clinics.Manage). Only what the Domain supports: register, activate and deactivate.
    // There is no update and no delete.
    [ApiController]
    [Route("api/web/admin/clinics")]
    [Authorize(Policy = Permissions.Clinics.Manage)]
    public sealed class ClinicsController : ControllerBase
    {
        private readonly ClinicalOrganizationService _organization;

        public ClinicsController(ClinicalOrganizationService organization) => _organization = organization;

        [HttpGet]
        [ProducesResponseType<IReadOnlyList<ClinicAdminSummary>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> List(string? search, ClinicStatus? status, CancellationToken cancellationToken) =>
            Ok(await _organization.ListClinicsAsync(search, status, cancellationToken));

        [HttpGet("{id:guid}")]
        [ProducesResponseType<ClinicAdminSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
            Ok(await _organization.GetClinicAsync(id, cancellationToken));

        [HttpPost]
        [ProducesResponseType<ClinicAdminSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Register(RegisterClinicRequest request, CancellationToken cancellationToken)
        {
            var clinic = await _organization.RegisterClinicAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = clinic.Id }, clinic);
        }

        [HttpPost("{id:guid}/activate")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
        {
            await _organization.SetClinicActiveAsync(id, active: true, cancellationToken);
            return NoContent();
        }

        // Open affiliations are not ended: an inactive clinic simply stops being a valid clinical context.
        [HttpPost("{id:guid}/deactivate")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
        {
            await _organization.SetClinicActiveAsync(id, active: false, cancellationToken);
            return NoContent();
        }
    }
}
