using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Enums.Organizations;

namespace SCI_PH.Web.BFF.Controllers.Admin
{
    // Clinical staff and their clinic affiliations (ClinicalStaff.Manage). No update and no delete; affiliations are ended,
    // never removed. The account of a staff member is created through POST /api/web/admin/invitations/staff (Users.Manage).
    [ApiController]
    [Route("api/web/admin/staff")]
    [Authorize(Policy = Permissions.ClinicalStaff.Manage)]
    public sealed class StaffController : ControllerBase
    {
        private readonly ClinicalOrganizationService _organization;

        public StaffController(ClinicalOrganizationService organization) => _organization = organization;

        [HttpGet]
        [ProducesResponseType<IReadOnlyList<ClinicalStaffAdminSummary>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> List(string? search, StaffStatus? status, CancellationToken cancellationToken) =>
            Ok(await _organization.ListStaffAsync(search, status, cancellationToken));

        [HttpGet("{id:guid}")]
        [ProducesResponseType<ClinicalStaffAdminSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
            Ok(await _organization.GetStaffAsync(id, cancellationToken));

        [HttpPost]
        [ProducesResponseType<ClinicalStaffAdminSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Register(RegisterClinicalStaffRequest request, CancellationToken cancellationToken)
        {
            var staff = await _organization.RegisterStaffAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = staff.Id }, staff);
        }

        [HttpPost("{id:guid}/activate")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
        {
            await _organization.SetStaffActiveAsync(id, active: true, cancellationToken);
            return NoContent();
        }

        // The linked account is not touched; the staff member simply stops acting clinically.
        [HttpPost("{id:guid}/deactivate")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
        {
            await _organization.SetStaffActiveAsync(id, active: false, cancellationToken);
            return NoContent();
        }

        [HttpGet("{id:guid}/affiliations")]
        [ProducesResponseType<IReadOnlyList<ClinicStaffAffiliationSummary>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ListAffiliations(Guid id, CancellationToken cancellationToken) =>
            Ok(await _organization.ListAffiliationsAsync(id, cancellationToken));

        [HttpPost("{id:guid}/affiliations")]
        [ProducesResponseType<ClinicStaffAffiliationSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> StartAffiliation(Guid id, StartAffiliationRequest request, CancellationToken cancellationToken)
        {
            var affiliation = await _organization.StartAffiliationAsync(id, request, cancellationToken);
            return CreatedAtAction(nameof(ListAffiliations), new { id }, affiliation);
        }

        [HttpPost("{id:guid}/affiliations/{affiliationId:guid}/end")]
        [ProducesResponseType<ClinicStaffAffiliationSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> EndAffiliation(Guid id, Guid affiliationId, CancellationToken cancellationToken) =>
            Ok(await _organization.EndAffiliationAsync(id, affiliationId, cancellationToken));
    }
}
