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
        private static readonly ClinicalNoteType[] WebNoteTypes =
            [ClinicalNoteType.Assessment, ClinicalNoteType.Evolution, ClinicalNoteType.Other];

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

        // ClinicalEncounter.ChangeStatus(Closed), with an optional discharge note. 409 when the status does not allow it.
        [HttpPost("{id:guid}/close")]
        [Authorize(Policy = Permissions.Encounters.Close)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Close(Guid id, CloseEncounterRequest request, CancellationToken cancellationToken)
        {
            await _encounters.CloseAsync(id, request, cancellationToken);
            return NoContent();
        }

        [HttpGet("{id:guid}/notes")]
        [Authorize(Policy = Permissions.Encounters.Read)]
        [ProducesResponseType<IReadOnlyList<ClinicalNoteSummary>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> ListNotes(Guid id, CancellationToken cancellationToken) =>
            Ok(await _records.ListNotesAsync(id, cancellationToken));

        [HttpPost("{id:guid}/notes")]
        [Authorize(Policy = Permissions.ClinicalNotes.Write)]
        [ProducesResponseType<ClinicalNoteSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RecordNote(Guid id, RecordClinicalNoteRequest request, CancellationToken cancellationToken)
        {
            // The hospital web writes assessment, evolution and other notes. Discharge notes are written by /close;
            // prehospital and transfer summaries belong to the paramedic and transfer flows.
            if (!WebNoteTypes.Contains(request.Type))
            {
                throw new RequestValidationException($"{request.Type} notes cannot be written from this endpoint.", "type");
            }

            var note = await _records.RecordNoteAsync(id, request, cancellationToken);
            return CreatedAtAction(nameof(ListNotes), new { id }, note);
        }

        [HttpGet("{id:guid}/diagnoses")]
        [Authorize(Policy = Permissions.Encounters.Read)]
        [ProducesResponseType<IReadOnlyList<DiagnosisSummary>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> ListDiagnoses(Guid id, CancellationToken cancellationToken) =>
            Ok(await _records.ListDiagnosesAsync(id, cancellationToken));

        [HttpPost("{id:guid}/diagnoses")]
        [Authorize(Policy = Permissions.Diagnoses.Write)]
        [ProducesResponseType<DiagnosisSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RecordDiagnosis(Guid id, RecordDiagnosisRequest request, CancellationToken cancellationToken)
        {
            var diagnosis = await _records.RecordDiagnosisAsync(id, request, cancellationToken);
            return CreatedAtAction(nameof(ListDiagnoses), new { id }, diagnosis);
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
