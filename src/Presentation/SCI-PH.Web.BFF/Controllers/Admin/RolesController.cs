using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Common.Security;

namespace SCI_PH.Web.BFF.Controllers.Admin
{
    public sealed record RoleResponse(string Name, IReadOnlyList<string> Permissions);

    [ApiController]
    [Route("api/web/admin/roles")]
    [Authorize(Policy = Permissions.Users.Manage)]
    public sealed class RolesController : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType<IReadOnlyList<RoleResponse>>(StatusCodes.Status200OK)]
        public IActionResult List() =>
            Ok(SecurityRoles.All.Select(role => new RoleResponse(role, RolePermissions.For(role))).ToArray());
    }
}
