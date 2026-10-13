using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;

namespace SCI_PH.Web.BFF.Controllers.Clinical
{
    // Prescriptions and medication administrations of an encounter. Together they are the Kardex: there is no separate
    // Kardex resource, the client composes it from these two lists.
    [ApiController]
    [Route("api/web/encounters")]
    public sealed class EncounterMedicationsController : ControllerBase
    {
        private readonly MedicationService _medications;

        public EncounterMedicationsController(MedicationService medications) => _medications = medications;

        [HttpGet("{id:guid}/prescriptions")]
        [Authorize(Policy = Permissions.Encounters.Read)]
        [ProducesResponseType<IReadOnlyList<PrescriptionSummary>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> ListPrescriptions(Guid id, CancellationToken cancellationToken) =>
            Ok(await _medications.ListPrescriptionsAsync(id, cancellationToken));

        // Draft + details + Issue in one transaction: the response is the issued prescription.
        [HttpPost("{id:guid}/prescriptions")]
        [Authorize(Policy = Permissions.Prescriptions.Write)]
        [ProducesResponseType<PrescriptionSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> CreatePrescription(Guid id, CreatePrescriptionRequest request, CancellationToken cancellationToken)
        {
            var prescription = await _medications.CreatePrescriptionAsync(id, request, cancellationToken);
            return CreatedAtAction(nameof(ListPrescriptions), new { id }, prescription);
        }

        [HttpGet("{id:guid}/medication-administrations")]
        [Authorize(Policy = Permissions.Encounters.Read)]
        [ProducesResponseType<IReadOnlyList<MedicationAdministrationSummary>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> ListAdministrations(Guid id, CancellationToken cancellationToken) =>
            Ok(await _medications.ListAdministrationsAsync(id, cancellationToken));

        [HttpPost("{id:guid}/medication-administrations")]
        [Authorize(Policy = Permissions.MedicationAdministrations.Write)]
        [ProducesResponseType<MedicationAdministrationSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RecordAdministration(
            Guid id, RecordMedicationAdministrationRequest request, CancellationToken cancellationToken)
        {
            var administration = await _medications.RecordAdministrationAsync(id, request, cancellationToken);
            return CreatedAtAction(nameof(ListAdministrations), new { id }, administration);
        }
    }
}
