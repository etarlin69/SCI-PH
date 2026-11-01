using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;

namespace SCI_PH.Mobile.BFF.Controllers
{
    // The signed-in patient's own record (ADR-015), read-only. No route takes a patient id: PatientSelfService resolves
    // it from the account (Patient.IdentityUserId). The PatientSelfService policy is only the entry gate (a Patient
    // account; staff accounts never get in); the resource boundary is the service: 403 without a clinical link, 404 for
    // an encounter that is not the patient's own.
    [ApiController]
    [Route("api/mobile/patient/me")]
    [Authorize(Policy = AccessPolicies.PatientSelfService)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class PatientSelfController : ControllerBase
    {
        private readonly PatientSelfService _self;

        public PatientSelfController(PatientSelfService self) => _self = self;

        [HttpGet]
        [ProducesResponseType<PatientSelfProfile>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Profile(CancellationToken cancellationToken) =>
            Ok(await _self.GetProfileAsync(cancellationToken));

        [HttpGet("summary")]
        [ProducesResponseType<PatientSelfSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Summary(CancellationToken cancellationToken) =>
            Ok(await _self.GetSummaryAsync(cancellationToken));

        [HttpGet("encounters")]
        [ProducesResponseType<IReadOnlyList<PatientSelfEncounter>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Encounters(CancellationToken cancellationToken) =>
            Ok(await _self.ListEncountersAsync(cancellationToken));

        [HttpGet("encounters/{encounterId:guid}")]
        [ProducesResponseType<PatientSelfEncounterDetail>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Encounter(Guid encounterId, CancellationToken cancellationToken) =>
            Ok(await _self.GetEncounterAsync(encounterId, cancellationToken));

        [HttpGet("allergies")]
        [ProducesResponseType<IReadOnlyList<PatientSelfAllergy>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Allergies(CancellationToken cancellationToken) =>
            Ok(await _self.ListAllergiesAsync(cancellationToken));

        [HttpGet("diagnoses")]
        [ProducesResponseType<IReadOnlyList<PatientSelfDiagnosis>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Diagnoses(CancellationToken cancellationToken) =>
            Ok(await _self.ListDiagnosesAsync(cancellationToken));

        [HttpGet("prescriptions")]
        [ProducesResponseType<IReadOnlyList<PatientSelfPrescription>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Prescriptions(CancellationToken cancellationToken) =>
            Ok(await _self.ListPrescriptionsAsync(cancellationToken));

        [HttpGet("results")]
        [ProducesResponseType<IReadOnlyList<PatientSelfResult>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Results(CancellationToken cancellationToken) =>
            Ok(await _self.ListResultsAsync(cancellationToken));
    }
}
