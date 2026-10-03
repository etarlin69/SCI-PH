using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Devices;
using SCI_PH.Web.BFF.Security;

namespace SCI_PH.Web.BFF.Controllers.Admin
{
    // Device credentials for the Device API. The key is returned only once, at creation or rotation.
    [ApiController]
    [Route("api/web/admin/devices")]
    [Authorize(Policy = Permissions.Devices.Manage)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class DevicesController : ControllerBase
    {
        private readonly DeviceCredentialService _devices;
        private readonly ICurrentUser _currentUser;

        public DevicesController(DeviceCredentialService devices, ICurrentUser currentUser)
        {
            _devices = devices;
            _currentUser = currentUser;
        }

        private Guid ActorId => _currentUser.UserId!.Value;

        [HttpGet]
        [ProducesResponseType<IReadOnlyList<DeviceSummary>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> List(CancellationToken cancellationToken) => Ok(await _devices.ListAsync(cancellationToken));

        [HttpPost]
        [ProducesResponseType<DeviceSecret>(StatusCodes.Status201Created)]
        public async Task<IActionResult> Provision(ProvisionDeviceRequest request, CancellationToken cancellationToken) =>
            ToResult(await _devices.ProvisionAsync(request, ActorId, HttpContext.ToClientContext(), cancellationToken), StatusCodes.Status201Created);

        [HttpPost("{id:guid}/rotate-secret")]
        [ProducesResponseType<DeviceSecret>(StatusCodes.Status200OK)]
        public async Task<IActionResult> RotateSecret(Guid id, CancellationToken cancellationToken) =>
            ToResult(await _devices.RotateSecretAsync(id, ActorId, HttpContext.ToClientContext(), cancellationToken), StatusCodes.Status200OK);

        [HttpPost("{id:guid}/revoke")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken) =>
            await _devices.RevokeAsync(id, ActorId, HttpContext.ToClientContext(), cancellationToken) == DeviceOperationOutcome.Succeeded
                ? NoContent()
                : NotFound();

        private IActionResult ToResult(DeviceSecretResult result, int successStatus) =>
            result.Outcome switch
            {
                DeviceOperationOutcome.Succeeded => StatusCode(successStatus, result.Secret),
                DeviceOperationOutcome.NotFound => NotFound(),
                DeviceOperationOutcome.DuplicateCode => Problem(statusCode: StatusCodes.Status409Conflict, title: "The device code is already registered."),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: $"Scopes must be a non-empty subset of: {string.Join(", ", DeviceScopes.All)}.")
            };
    }
}
