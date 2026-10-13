using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;

namespace SCI_PH.Web.BFF.Controllers.Clinical
{
    // Global medication catalog (read only). Prescriptions and administrations reference its entries.
    [ApiController]
    [Route("api/web/medications")]
    public sealed class MedicationsController : ControllerBase
    {
        private readonly MedicationService _medications;

        public MedicationsController(MedicationService medications) => _medications = medications;

        [HttpGet]
        [Authorize(Policy = Permissions.Medications.Read)]
        [ProducesResponseType<IReadOnlyList<MedicationSummary>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> List(CancellationToken cancellationToken) =>
            Ok(await _medications.ListMedicationsAsync(cancellationToken));
    }
}
