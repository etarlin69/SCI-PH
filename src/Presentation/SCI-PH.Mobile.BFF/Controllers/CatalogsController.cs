using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;

namespace SCI_PH.Mobile.BFF.Controllers
{
    // Catalogs of the paramedic app. Emergencies.Operate opens the module (controller level), as for the emergencies.
    // The procedure catalog holds no patient data and is only used to record a procedure, so it takes the permission of
    // that write (Procedures.Write): there is no Procedures.Read. No role is checked here.
    [ApiController]
    [Route("api/mobile/catalogs")]
    [Authorize(Policy = Permissions.Emergencies.Operate)]
    public sealed class CatalogsController : ControllerBase
    {
        private readonly PrehospitalEmergencyService _emergencies;

        public CatalogsController(PrehospitalEmergencyService emergencies) => _emergencies = emergencies;

        [HttpGet("procedures")]
        [Authorize(Policy = Permissions.Procedures.Write)]
        [ProducesResponseType<IReadOnlyList<PrehospitalProcedureCatalogItem>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Procedures(CancellationToken cancellationToken) =>
            Ok(await _emergencies.ListProcedureCatalogAsync(cancellationToken));
    }
}
