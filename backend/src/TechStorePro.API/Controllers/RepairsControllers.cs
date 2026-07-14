using MediatR;
using Microsoft.AspNetCore.Mvc;
using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Repairs.Invoicing;
using TechStorePro.Application.Repairs.Queries;
using TechStorePro.Application.Repairs.Services;
using TechStorePro.Application.Repairs.Tickets;
using TechStorePro.Application.Repairs.Warranties;
using TechStorePro.Application.Repairs.Work;
using TechStorePro.Domain.Repairs;

namespace TechStorePro.API.Controllers;

/// <summary>
/// Repair jobs (requirements §28) — the workshop.
///
/// The transitions are separate endpoints rather than a PATCH on a status field, for the same reason the
/// rest of the system works this way: <c>PATCH {status: "InRepair"}</c> invites a client to move a job
/// wherever it likes, and the state machine would be enforced only by whichever handler remembered to.
/// Each verb below is a thing that happens in the shop.
/// </summary>
[Route("api/v1/repairs")]
public class RepairsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<RepairTicketDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] RepairTicketStatus? status = null,
        [FromQuery] Guid? customerId = null,
        [FromQuery] Guid? technicianId = null,
        [FromQuery] bool openOnly = false) =>
        Ok(await Mediator.Send(
            new GetRepairTicketsQuery(page, pageSize, search, status, customerId, technicianId, openOnly)));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RepairTicketDto>> Get(Guid id) =>
        Ok(await Mediator.Send(new GetRepairTicketByIdQuery(id)));

    /// <summary>Book a device in over the counter. No stock moves — the machine is the customer's.</summary>
    [HttpPost]
    public async Task<ActionResult<Guid>> BookIn(BookInDeviceCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { id }, id);
    }

    [HttpPost("{id:guid}/diagnose")]
    public async Task<IActionResult> BeginDiagnosis(Guid id, BeginDiagnosisCommand command)
    {
        if (id != command.RepairTicketId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    /// <summary>
    /// The findings and the estimate. A chargeable job now waits for the customer; a warranty job goes
    /// straight to the bench, because there is no price for anyone to agree to.
    /// </summary>
    [HttpPost("{id:guid}/diagnosis")]
    public async Task<IActionResult> RecordDiagnosis(Guid id, RecordDiagnosisCommand command)
    {
        if (id != command.RepairTicketId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    /// <summary>The customer said yes. This is what unlocks the parts store.</summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, ApproveEstimateCommand command)
    {
        if (id != command.RepairTicketId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    /// <summary>The customer said no. The device goes back untouched.</summary>
    [HttpPost("{id:guid}/decline")]
    public async Task<IActionResult> Decline(Guid id, DeclineEstimateCommand command)
    {
        if (id != command.RepairTicketId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> BeginTesting(Guid id, BeginTestingCommand command)
    {
        if (id != command.RepairTicketId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    [HttpPost("{id:guid}/ready")]
    public async Task<IActionResult> MarkReady(Guid id, MarkReadyCommand command)
    {
        if (id != command.RepairTicketId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    /// <summary>The customer collects the machine. It does not require the bill to be paid.</summary>
    [HttpPost("{id:guid}/deliver")]
    public async Task<IActionResult> Deliver(Guid id, DeliverDeviceCommand command)
    {
        if (id != command.RepairTicketId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancelRepairCommand command)
    {
        if (id != command.RepairTicketId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    // --- Parts, labour and the vendor ---------------------------------------------------------------

    /// <summary>
    /// Fit a part. <b>This is the only thing in the repairs module that moves stock</b>, and it moves it
    /// now rather than at invoicing — the part is physically inside the customer's machine (§45 D9).
    /// </summary>
    [HttpPost("{id:guid}/parts")]
    public async Task<ActionResult<Guid>> ConsumePart(Guid id, ConsumePartCommand command)
    {
        if (id != command.RepairTicketId)
        {
            return BadRequest("Route id and body id differ.");
        }

        return Ok(await Mediator.Send(command));
    }

    /// <summary>Take a part back out and put it on the shelf — a RepairReturn movement, not an undo.</summary>
    [HttpPost("parts/{partId:guid}/return")]
    public async Task<IActionResult> ReturnPart(Guid partId, ReturnPartCommand command)
    {
        if (partId != command.RepairPartId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    [HttpPost("{id:guid}/labour")]
    public async Task<ActionResult<Guid>> LogLabour(Guid id, LogLabourCommand command)
    {
        if (id != command.RepairTicketId)
        {
            return BadRequest("Route id and body id differ.");
        }

        return Ok(await Mediator.Send(command));
    }

    /// <summary>Send the job out to a third party (§29). No stock moves; a cost lands on the ticket.</summary>
    [HttpPost("{id:guid}/outsource")]
    public async Task<ActionResult<Guid>> SendToVendor(Guid id, SendToVendorCommand command)
    {
        if (id != command.RepairTicketId)
        {
            return BadRequest("Route id and body id differ.");
        }

        return Ok(await Mediator.Send(command));
    }

    [HttpPost("outsourcing/{outsourcingId:guid}/receive")]
    public async Task<IActionResult> ReceiveFromVendor(Guid outsourcingId, ReceiveFromVendorCommand command)
    {
        if (outsourcingId != command.OutsourcingId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    [HttpPost("outsourcing/{outsourcingId:guid}/cancel")]
    public async Task<IActionResult> CancelOutsourcing(Guid outsourcingId, CancelOutsourcingCommand command)
    {
        if (outsourcingId != command.OutsourcingId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    /// <summary>
    /// Bill the job. It raises an ordinary sales invoice (§45 D11) and moves no stock — the parts left the
    /// shelf when they were fitted.
    /// </summary>
    [HttpPost("{id:guid}/invoice")]
    public async Task<ActionResult<Guid>> Bill(Guid id, BillRepairCommand command)
    {
        if (id != command.RepairTicketId)
        {
            return BadRequest("Route id and body id differ.");
        }

        return Ok(await Mediator.Send(command));
    }
}

/// <summary>
/// Warranties and the claims made on them (requirements §30).
/// </summary>
[Route("api/v1/warranties")]
public class WarrantiesController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<WarrantyDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null) =>
        Ok(await Mediator.Send(new GetWarrantiesQuery(page, pageSize, search)));

    /// <summary>
    /// "Is this machine still under warranty?" — asked from the counter, before anything is booked in,
    /// because the customer is standing there and wants to know.
    /// </summary>
    [HttpGet("check")]
    public async Task<ActionResult<WarrantyCover>> Check([FromQuery] string serialNumber) =>
        Ok(await Mediator.Send(new CheckWarrantyQuery(serialNumber)));

    /// <summary>
    /// Register a manufacturer's or a supplier's warranty. <b>The shop's own is not registered here</b> —
    /// P5 stamps it on the unit at the moment of sale, and a second copy could disagree with it.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Guid>> Register(RegisterWarrantyCommand command) =>
        Ok(await Mediator.Send(command));

    [HttpGet("claims")]
    public async Task<ActionResult<PagedResult<WarrantyClaimDto>>> Claims(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] WarrantyClaimStatus? status = null) =>
        Ok(await Mediator.Send(new GetWarrantyClaimsQuery(page, pageSize, search, status)));

    [HttpPost("claims/{claimId:guid}/accept")]
    public async Task<IActionResult> AcceptClaim(Guid claimId, AcceptWarrantyClaimCommand command)
    {
        if (claimId != command.WarrantyClaimId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    /// <summary>
    /// The fault was not covered. <b>This is the decision that makes the job chargeable</b> — the parts and
    /// labour already booked to it as warranty work become billable, and the shop stops eating them.
    /// </summary>
    [HttpPost("claims/{claimId:guid}/reject")]
    public async Task<IActionResult> RejectClaim(Guid claimId, RejectWarrantyClaimCommand command)
    {
        if (claimId != command.WarrantyClaimId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }
}
