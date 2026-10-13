using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Clinical;
using SCI_PH.Application.Features.Clinical;

namespace SCI_PH.Web.BFF.Controllers.Clinical
{
    // Transfers of an encounter between clinics. Request and dispatch belong to the origin clinic, reception to the
    // destination stored in the transfer; the acting staff always comes from the session.
    [ApiController]
    [Route("api/web/transfers")]
    public sealed class TransfersController : ControllerBase
    {
        private readonly TransferService _transfers;

        public TransfersController(TransferService transfers) => _transfers = transfers;

        // Requested or InTransit transfers to the user's clinics. clinicId narrows it to one affiliation.
        [HttpGet("incoming")]
        [Authorize(Policy = Permissions.Transfers.Read)]
        [ProducesResponseType<IReadOnlyList<ClinicalTransferSummary>>(StatusCodes.Status200OK)]
        public async Task<IActionResult> ListIncoming(Guid? clinicId, CancellationToken cancellationToken) =>
            Ok(await _transfers.ListIncomingAsync(clinicId, cancellationToken));

        [HttpGet("{id:guid}")]
        [Authorize(Policy = Permissions.Transfers.Read)]
        [ProducesResponseType<ClinicalTransferSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
            Ok(await _transfers.GetAsync(id, cancellationToken));

        // ClinicalTransfer.Request: the encounter keeps its status until the dispatch.
        [HttpPost("/api/web/encounters/{encounterId:guid}/transfers")]
        [Authorize(Policy = Permissions.Transfers.Manage)]
        [ProducesResponseType<ClinicalTransferSummary>(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RequestTransfer(Guid encounterId, RequestTransferRequest request, CancellationToken cancellationToken)
        {
            var transfer = await _transfers.RequestAsync(encounterId, request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = transfer.Id }, transfer);
        }

        // ClinicalTransfer.Dispatch + encounter Transferred, from the origin clinic.
        [HttpPost("{id:guid}/dispatch")]
        [Authorize(Policy = Permissions.Transfers.Manage)]
        [ProducesResponseType<ClinicalTransferSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Dispatch(Guid id, CancellationToken cancellationToken) =>
            Ok(await _transfers.DispatchAsync(id, cancellationToken));

        // ClinicalTransfer.Cancel from the origin clinic; an InTransit transfer also returns the encounter to InProgress.
        [HttpPost("{id:guid}/cancel")]
        [Authorize(Policy = Permissions.Transfers.Manage)]
        [ProducesResponseType<ClinicalTransferSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
            Ok(await _transfers.CancelAsync(id, cancellationToken));

        // ClinicalTransfer.Receive + ClinicalEncounter.ReceiveAtClinic, from the destination clinic.
        [HttpPost("{id:guid}/receive")]
        [Authorize(Policy = Permissions.Transfers.Manage)]
        [ProducesResponseType<ClinicalTransferSummary>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Receive(Guid id, CancellationToken cancellationToken) =>
            Ok(await _transfers.ReceiveAsync(id, cancellationToken));
    }
}
