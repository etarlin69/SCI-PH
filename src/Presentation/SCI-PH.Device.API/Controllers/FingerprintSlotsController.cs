using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Biometrics;
using SCI_PH.Application.Features.Devices;
using SCI_PH.Infrastructure.Web;

namespace SCI_PH.Device.API.Controllers
{
    // The reader asks which slots of its own sensor SCI-PH considers occupied, to find orphan templates (ADR-010 §10).
    // Requires Device.Fingerprint.Enroll: the capability that writes and deletes slots. The reader comes only from its
    // credential (no device, reference or patient parameter); the answer is only slot numbers.
    [ApiController]
    [Route("api/device/fingerprint/slots")]
    [Authorize(Policy = DeviceScopes.FingerprintEnroll)]
    [EnableRateLimiting(RateLimitPolicies.Device)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class FingerprintSlotsController : ControllerBase
    {
        private readonly FingerprintSlotInventoryReaderService _inventory;

        public FingerprintSlotsController(FingerprintSlotInventoryReaderService inventory) => _inventory = inventory;

        private Guid ReaderId => Guid.Parse(User.FindFirst(CustomClaimTypes.DeviceId)!.Value);

        private string ReaderCode => User.FindFirst(CustomClaimTypes.DeviceCode)!.Value;

        // Always 200: {"slots":[...]}, unique and ascending, empty when the reader has none.
        [HttpGet]
        [ProducesResponseType<FingerprintSlotInventory>(StatusCodes.Status200OK)]
        public async Task<FingerprintSlotInventory> Get(CancellationToken cancellationToken) =>
            await _inventory.GetAsync(ReaderId, ReaderCode, cancellationToken);
    }
}
