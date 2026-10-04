using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Accounts;
using SCI_PH.Web.BFF.Security;

namespace SCI_PH.Web.BFF.Controllers.Admin
{
    // Account administration. Only predefined roles can be assigned; every change ends the account sessions.
    [ApiController]
    [Route("api/web/admin/users")]
    [Authorize(Policy = Permissions.Users.Manage)]
    public sealed class UsersController : ControllerBase
    {
        private readonly UserAccountService _accounts;
        private readonly ICurrentUser _currentUser;

        public UsersController(UserAccountService accounts, ICurrentUser currentUser)
        {
            _accounts = accounts;
            _currentUser = currentUser;
        }

        private Guid ActorId => _currentUser.UserId!.Value;

        [HttpGet]
        [ProducesResponseType<UserPage>(StatusCodes.Status200OK)]
        public async Task<IActionResult> List(string? search, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) =>
            Ok(await _accounts.ListAsync(search, page, pageSize, cancellationToken));

        [HttpGet("{id:guid}")]
        [ProducesResponseType<UserSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
            await _accounts.GetAsync(id, cancellationToken) is { } user ? Ok(user) : NotFound();

        [HttpPost("{id:guid}/activate")]
        public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
            ToResult(await _accounts.ActivateAsync(id, ActorId, HttpContext.ToClientContext(), cancellationToken));

        [HttpPost("{id:guid}/deactivate")]
        public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
            ToResult(await _accounts.DeactivateAsync(id, ActorId, HttpContext.ToClientContext(), cancellationToken));

        [HttpPut("{id:guid}/roles")]
        public async Task<IActionResult> SetRoles(Guid id, SetRolesRequest request, CancellationToken cancellationToken) =>
            ToResult(await _accounts.SetRolesAsync(id, request.Roles, ActorId, HttpContext.ToClientContext(), cancellationToken));

        [HttpPost("{id:guid}/revoke-sessions")]
        public async Task<IActionResult> RevokeSessions(Guid id, CancellationToken cancellationToken) =>
            ToResult(await _accounts.RevokeSessionsAsync(id, ActorId, HttpContext.ToClientContext(), cancellationToken));

        private IActionResult ToResult(UserAdministrationOutcome outcome) =>
            outcome switch
            {
                UserAdministrationOutcome.Succeeded => NoContent(),
                UserAdministrationOutcome.NotFound => NotFound(),
                UserAdministrationOutcome.InvalidRoles => Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Unknown roles, or Patient combined with other roles."),
                UserAdministrationOutcome.LastAdministrator => Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "The last active administrator cannot lose that role or be deactivated."),
                _ => Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "Administrators cannot remove their own administrator access.")
            };
    }
}
