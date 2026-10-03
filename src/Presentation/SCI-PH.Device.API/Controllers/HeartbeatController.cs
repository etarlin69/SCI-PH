using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;
using SCI_PH.Application.Features.Devices;
using SCI_PH.Infrastructure.Web;

namespace SCI_PH.Device.API.Controllers
{
    // The device identity comes from its credential, not from the body.
    [ApiController]
    [Route("api/device/heartbeat")]
    [Authorize(Policy = DeviceScopes.Heartbeat)]
    [EnableRateLimiting(RateLimitPolicies.Device)]
    public sealed class HeartbeatController : ControllerBase
    {
        private readonly DeviceAuthenticationService _devices;

        public HeartbeatController(DeviceAuthenticationService devices) => _devices = devices;

        [HttpPost]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Post(DeviceHeartbeatRequest request, CancellationToken cancellationToken)
        {
            var deviceId = Guid.Parse(User.FindFirst(CustomClaimTypes.DeviceId)!.Value);
            await _devices.RecordHeartbeatAsync(deviceId, request, cancellationToken);
            return NoContent();
        }
    }
}
