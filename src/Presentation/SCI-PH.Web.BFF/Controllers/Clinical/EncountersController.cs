using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Common.Errors;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;
using SCI_PH.Domain.Enums.Encounters;

namespace SCI_PH.Web.BFF.Controllers.Clinical
{
    // Clinical encounters of the user's clinics. Reading is clinical (Encounters.Read); reception uses the
    // administrative projection in AdmissionsController instead.
    [ApiController]
    [Route("api/web/encounters")]
    public sealed class EncountersController : ControllerBase
    {
        private readonly EncounterService _encounters;
        private readonly EncounterRecordService _records;

        public EncountersController(EncounterService encounters, EncounterRecordService records)
        {
            _encounters = encounters;
            _records = records;
        }

        // Open encounters of one clinic. clinicId is required when the user has several active affiliations.
        [HttpGet]
        [Authorize(Policy = Permissions.Encounters.Read)]
        [ProducesResponseType<IReadOnlyList<EncounterSummary>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> List(Guid? clinicId, CancellationToken cancellationToken) =>
            Ok(await _encounters.ListOpenAsync(clinicId, cancellationToken));

        [HttpGet("{id:guid}")]
        [Authorize(Policy = Permissions.Encounters.Read)]
        [ProducesResponseType<EncounterSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
            Ok(await _encounters.GetAsync(id, cancellationToken));

        // Returns the created summary so reception can continue without Encounters.Read.
        [HttpPost]
        [Authorize(Policy = Permissions.Encounters.Create)]
        [ProducesResponseType<EncounterSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Create(CreateEncounterRequest request, CancellationToken cancellationToken)
        {
            // Prehospital care is opened by the paramedic channel (Mobile BFF), never from the hospital web.
            if (request.Type == ClinicalEncounterType.Prehospital)
            {
                throw new RequestValidationException("Prehospital encounters cannot be created from the web.", "type");
            }

            var encounter = await _encounters.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = encounter.Id }, encounter);
        }

        [HttpPost("{id:guid}/identify")]
        [Authorize(Policy = Permissions.Patients.Identify)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Identify(Guid id, IdentifyEncounterPatientRequest request, CancellationToken cancellationToken)
        {
            await _encounters.IdentifyPatientAsync(id, request, cancellationToken);
            return NoContent();
        }

        [HttpGet("{id:guid}/triages")]
        [Authorize(Policy = Permissions.Encounters.Read)]
        [ProducesResponseType<IReadOnlyList<TriageSummary>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> ListTriages(Guid id, CancellationToken cancellationToken) =>
            Ok(await _records.ListTriagesAsync(id, cancellationToken));

        // Measurements inside a triage additionally require VitalSigns.Write (checked by the use case).
        [HttpPost("{id:guid}/triages")]
        [Authorize(Policy = Permissions.Triage.Write)]
        [ProducesResponseType<TriageSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RecordTriage(Guid id, RecordTriageRequest request, CancellationToken cancellationToken)
        {
            var triage = await _records.RecordTriageAsync(id, request, cancellationToken);
            return CreatedAtAction(nameof(ListTriages), new { id }, triage);
        }

        [HttpGet("{id:guid}/vital-signs")]
        [Authorize(Policy = Permissions.Encounters.Read)]
        [ProducesResponseType<IReadOnlyList<VitalSignSummary>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> ListVitalSigns(Guid id, CancellationToken cancellationToken) =>
            Ok(await _records.ListVitalSignsAsync(id, cancellationToken));

        [HttpPost("{id:guid}/vital-signs")]
        [Authorize(Policy = Permissions.VitalSigns.Write)]
        [ProducesResponseType<VitalSignSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RecordVitalSign(Guid id, RecordVitalSignRequest request, CancellationToken cancellationToken)
        {
            var vitalSign = await _records.RecordVitalSignAsync(id, request, cancellationToken);
            return CreatedAtAction(nameof(ListVitalSigns), new { id }, vitalSign);
        }
    }
}
