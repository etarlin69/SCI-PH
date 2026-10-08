using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;

namespace SCI_PH.Web.BFF.Controllers.Clinical
{
    // Patient registration and lookup. Identifiers (national id, passport) only travel in request bodies, never in URLs.
    [ApiController]
    [Route("api/web/patients")]
    public sealed class PatientsController : ControllerBase
    {
        private readonly PatientService _patients;

        public PatientsController(PatientService patients) => _patients = patients;

        [HttpPost]
        [Authorize(Policy = Permissions.Patients.Create)]
        [ProducesResponseType<PatientSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Register(RegisterPatientRequest request, CancellationToken cancellationToken)
        {
            var patient = await _patients.RegisterAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = patient.Id }, patient);
        }

        [HttpGet("{id:guid}")]
        [Authorize(Policy = Permissions.Patients.Read)]
        [ProducesResponseType<PatientSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
            Ok(await _patients.GetAsync(id, cancellationToken));

        [HttpPost("search")]
        [Authorize(Policy = Permissions.Patients.Read)]
        [ProducesResponseType<IReadOnlyList<PatientSummary>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Search(PatientSearchRequest request, CancellationToken cancellationToken) =>
            Ok(await _patients.SearchAsync(request, cancellationToken));
    }
}
