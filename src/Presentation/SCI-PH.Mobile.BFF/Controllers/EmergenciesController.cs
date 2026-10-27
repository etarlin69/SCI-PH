using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;

namespace SCI_PH.Mobile.BFF.Controllers
{
    // Prehospital emergencies of the paramedic app. Emergencies.Operate opens the module (controller level) and each
    // action also needs its granular permission; access to a concrete emergency is the paramedic's assignment
    // (PrehospitalEmergencyService / IEncounterAccessService). No role is checked here.
    [ApiController]
    [Route("api/mobile/emergencies")]
    [Authorize(Policy = Permissions.Emergencies.Operate)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class EmergenciesController : ControllerBase
    {
        private readonly PrehospitalEmergencyService _emergencies;

        public EmergenciesController(PrehospitalEmergencyService emergencies) => _emergencies = emergencies;

        // 201 the first time; 200 with the same emergency when the same clientRequestId is retried with the same data;
        // 409 when that clientRequestId was already used with different data.
        [HttpPost]
        [Authorize(Policy = Permissions.Encounters.Create)]
        [ProducesResponseType<PrehospitalEmergencySummary>(StatusCodes.Status201Created)]
        [ProducesResponseType<PrehospitalEmergencySummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Open(OpenPrehospitalEmergencyRequest request, CancellationToken cancellationToken)
        {
            var result = await _emergencies.OpenAsync(request, cancellationToken);
            return result.Created
                ? CreatedAtAction(nameof(Get), new { id = result.Emergency.Id }, result.Emergency)
                : Ok(result.Emergency);
        }

        [HttpGet("{id:guid}")]
        [Authorize(Policy = Permissions.Encounters.Read)]
        [ProducesResponseType<PrehospitalEmergencySummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
            Ok(await _emergencies.GetAsync(id, cancellationToken));

        // Identifies the NN patient by document on the same emergency (both policies are required). 200 with the
        // identified emergency, also when the same clientRequestId is retried with the same document; 404 when no patient
        // holds that document (the emergency stays NN); 409 when the emergency is already identified or that
        // clientRequestId was used with different data.
        [HttpPost("{id:guid}/identify/document")]
        [Authorize(Policy = Permissions.Patients.Read)]
        [Authorize(Policy = Permissions.Patients.Identify)]
        [ProducesResponseType<PrehospitalEmergencySummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> IdentifyByDocument(
            Guid id, IdentifyEmergencyByDocumentRequest request, CancellationToken cancellationToken) =>
            Ok(await _emergencies.IdentifyByDocumentAsync(id, request, cancellationToken));

        // One reading of 1 to 7 vital signs, all or nothing. 201 the first time (no Location: a reading has no resource of
        // its own); 200 with the same reading when the same clientRequestId is retried with the same data; 409 when that
        // clientRequestId was used with different data or the emergency no longer accepts records.
        [HttpPost("{id:guid}/vital-signs")]
        [Authorize(Policy = Permissions.VitalSigns.Write)]
        [ProducesResponseType<EmergencyVitalSignsSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType<EmergencyVitalSignsSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RecordVitalSigns(
            Guid id, RecordEmergencyVitalSignsRequest request, CancellationToken cancellationToken)
        {
            var result = await _emergencies.RecordVitalSignsAsync(id, request, cancellationToken);
            return result.Created ? StatusCode(StatusCodes.Status201Created, result.Reading) : Ok(result.Reading);
        }

        // One completed procedure of the catalog (GET api/mobile/catalogs/procedures). 201 the first time (no Location: there
        // is no endpoint that reads a single procedure); 200 with the same procedure when the same clientRequestId is
        // retried with the same data; 409 when that clientRequestId was used with different data or the emergency no
        // longer accepts records.
        [HttpPost("{id:guid}/procedures")]
        [Authorize(Policy = Permissions.Procedures.Write)]
        [ProducesResponseType<EmergencyProcedureSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType<EmergencyProcedureSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RecordProcedure(
            Guid id, RecordEmergencyProcedureRequest request, CancellationToken cancellationToken)
        {
            var result = await _emergencies.RecordProcedureAsync(id, request, cancellationToken);
            return result.Created ? StatusCode(StatusCodes.Status201Created, result.Procedure) : Ok(result.Procedure);
        }

        // One dose already given, of an active medication of the catalog (GET api/mobile/catalogs/medications), without
        // prescription. 201 the first time (no Location: there is no endpoint that reads a single administration); 200 with
        // the same administration when the same clientRequestId is retried with the same data; 404 for an unknown
        // medication; 409 for an inactive medication, a clientRequestId used with different data or an emergency that no
        // longer accepts records.
        [HttpPost("{id:guid}/medication-administrations")]
        [Authorize(Policy = Permissions.MedicationAdministrations.Write)]
        [ProducesResponseType<EmergencyMedicationAdministrationSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType<EmergencyMedicationAdministrationSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RecordMedicationAdministration(
            Guid id, RecordEmergencyMedicationAdministrationRequest request, CancellationToken cancellationToken)
        {
            var result = await _emergencies.RecordMedicationAdministrationAsync(id, request, cancellationToken);
            return result.Created ? StatusCode(StatusCodes.Status201Created, result.Administration) : Ok(result.Administration);
        }

        // One observation of the paramedic (type Prehospital, fixed by the server). 201 the first time (no Location: there is
        // no endpoint that reads a single note); 200 with the same note when the same clientRequestId is retried with the
        // same data; 409 when that clientRequestId was used with different data or the emergency no longer accepts records.
        [HttpPost("{id:guid}/notes")]
        [Authorize(Policy = Permissions.ClinicalNotes.Write)]
        [ProducesResponseType<EmergencyNoteSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType<EmergencyNoteSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RecordNote(Guid id, RecordEmergencyNoteRequest request, CancellationToken cancellationToken)
        {
            var result = await _emergencies.RecordNoteAsync(id, request, cancellationToken);
            return result.Created ? StatusCode(StatusCodes.Status201Created, result.Note) : Ok(result.Note);
        }
    }
}
