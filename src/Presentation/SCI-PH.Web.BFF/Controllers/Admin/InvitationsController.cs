using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Accounts;
using SCI_PH.Web.BFF.Security;

namespace SCI_PH.Web.BFF.Controllers.Admin
{
    // Invitations for existing clinical records. The raw token only travels in the email, never in a response.
    [ApiController]
    [Route("api/web/admin/invitations")]
    public sealed class InvitationsController : ControllerBase
    {
        private readonly AccountInvitationService _invitations;
        private readonly ICurrentUser _currentUser;

        public InvitationsController(AccountInvitationService invitations, ICurrentUser currentUser)
        {
            _invitations = invitations;
            _currentUser = currentUser;
        }

        private Guid ActorId => _currentUser.UserId!.Value;

        // Inviting again replaces any pending invitation of the same patient.
        [Authorize(Policy = Permissions.Accounts.InvitePatient)]
        [HttpPost("patients")]
        [ProducesResponseType<InvitationSummary>(StatusCodes.Status201Created)]
        public async Task<IActionResult> InvitePatient(InvitePatientRequest request, CancellationToken cancellationToken) =>
            ToResult(await _invitations.InvitePatientAsync(request.PatientId, ActorId, HttpContext.ToClientContext(), cancellationToken));

        [Authorize(Policy = Permissions.Users.Manage)]
        [HttpPost("staff")]
        [ProducesResponseType<InvitationSummary>(StatusCodes.Status201Created)]
        public async Task<IActionResult> InviteStaff(InviteStaffRequest request, CancellationToken cancellationToken) =>
            ToResult(await _invitations.InviteStaffAsync(request, ActorId, HttpContext.ToClientContext(), cancellationToken));

        [Authorize(Policy = Permissions.Users.Manage)]
        [HttpPost("{id:guid}/reissue")]
        [ProducesResponseType<InvitationSummary>(StatusCodes.Status201Created)]
        public async Task<IActionResult> Reissue(Guid id, CancellationToken cancellationToken) =>
            ToResult(await _invitations.ReissueAsync(id, ActorId, HttpContext.ToClientContext(), cancellationToken));

        [Authorize(Policy = Permissions.Users.Manage)]
        [HttpPost("{id:guid}/revoke")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken) =>
            await _invitations.RevokeAsync(id, ActorId, HttpContext.ToClientContext(), cancellationToken) ? NoContent() : NotFound();

        private IActionResult ToResult(InvitationResult result) =>
            result.Outcome switch
            {
                InvitationOutcome.Created => StatusCode(StatusCodes.Status201Created, result.Invitation),
                InvitationOutcome.SubjectNotFound or InvitationOutcome.InvitationNotFound => NotFound(),
                InvitationOutcome.InvalidRole => Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: $"Role must be one of: {string.Join(", ", SecurityRoles.InvitableStaff)}."),
                InvitationOutcome.SubjectHasNoEmail => Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "The patient has no active email identifier."),
                _ => Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "The record is already linked, the email is in use or the invitation is no longer pending.")
            };
    }
}
