using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Inventory.Adjustments;
using TechStorePro.Application.Inventory.Barcodes;
using TechStorePro.Application.Inventory.Counts;
using TechStorePro.Application.Inventory.Queries;
using TechStorePro.Application.Inventory.Reservations;
using TechStorePro.Application.Inventory.Serials;
using TechStorePro.Application.Inventory.Transfers;
using Microsoft.AspNetCore.Mvc;

namespace TechStorePro.API.Controllers;

/// <summary>Stock on hand, the ledger behind it, and the reports over both (requirements §19, §35).</summary>
[Route("api/v1/inventory")]
public class InventoryController : ApiControllerBase
{
    /// <summary>What is on the shelf. <c>?lowStock=true</c> is the reorder report of requirements §36.</summary>
    [HttpGet("stock")]
    public async Task<ActionResult<PagedResult<StockBalanceDto>>> Stock([FromQuery] GetStockQuery query) =>
        Ok(await Mediator.Send(query));

    /// <summary>The ledger itself — every movement, with the running balance and average after each.</summary>
    [HttpGet("movements")]
    public async Task<ActionResult<PagedResult<StockMovementDto>>> Movements(
        [FromQuery] GetStockMovementsQuery query) =>
        Ok(await Mediator.Send(query));

    /// <summary>
    /// Stock as it stood on a past date, replayed from the ledger (requirements §19). Opening,
    /// purchases, sales, transfers, adjustments, repairs, closing.
    /// </summary>
    [HttpGet("historical")]
    public async Task<ActionResult<HistoricalStockDto>> Historical([FromQuery] GetHistoricalStockQuery query) =>
        Ok(await Mediator.Send(query));

    /// <summary>What the stock is worth, at weighted-average cost, per warehouse (requirements §35).</summary>
    [HttpGet("valuation")]
    public async Task<ActionResult<StockValuationDto>> Valuation([FromQuery] Guid? warehouseId) =>
        Ok(await Mediator.Send(new GetStockValuationQuery(warehouseId)));

    /// <summary>
    /// Recomputes every balance from the ledger and reports any that disagree (architecture.md §4.5).
    /// A nightly job calls this. If it ever returns a discrepancy, something wrote stock outside
    /// <c>IStockLedger</c> — which is the one failure the whole module exists to prevent.
    /// </summary>
    [HttpGet("balance-audit")]
    public async Task<ActionResult<BalanceAuditDto>> BalanceAudit() =>
        Ok(await Mediator.Send(new GetBalanceAuditQuery()));
}

/// <summary>Stock adjustments — writing stock on or off, with a reason (requirements §19).</summary>
[Route("api/v1/inventory/adjustments")]
public class StockAdjustmentsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdjustmentDto>>> List([FromQuery] GetAdjustmentsQuery query) =>
        Ok(await Mediator.Send(query));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdjustmentDto>> Get(Guid id) =>
        Ok(await Mediator.Send(new GetAdjustmentQuery(id)));

    /// <summary>Posts to the ledger immediately. There is no draft and no approval — see FeatureCatalog.</summary>
    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateAdjustmentCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { id }, id);
    }
}

/// <summary>Stock transfers between warehouses (requirements §19).</summary>
[Route("api/v1/inventory/transfers")]
public class StockTransfersController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<TransferDto>>> List([FromQuery] GetTransfersQuery query) =>
        Ok(await Mediator.Send(query));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TransferDto>> Get(Guid id) =>
        Ok(await Mediator.Send(new GetTransferQuery(id)));

    /// <summary>Raises a draft. Nothing moves until it ships.</summary>
    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateTransferCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { id }, id);
    }

    /// <summary>The van is loaded: stock leaves the source and is in transit, owned by neither end.</summary>
    [HttpPost("{id:guid}/ship")]
    public async Task<IActionResult> Ship(Guid id)
    {
        await Mediator.Send(new ShipTransferCommand(id));
        return NoContent();
    }

    /// <summary>
    /// Somebody signed for it. Posts what actually arrived — an omitted body means "all of it", and a
    /// short delivery is recorded as a shortfall rather than silently rounded away.
    /// </summary>
    [HttpPost("{id:guid}/receive")]
    public async Task<IActionResult> Receive(Guid id, ReceiveTransferCommand? command)
    {
        await Mediator.Send(command is null ? new ReceiveTransferCommand(id) : command with { Id = id });
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        await Mediator.Send(new CancelTransferCommand(id));
        return NoContent();
    }
}

/// <summary>Physical stock counts (requirements §21).</summary>
[Route("api/v1/inventory/counts")]
public class StockCountsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<CountDto>>> List([FromQuery] GetCountsQuery query) =>
        Ok(await Mediator.Send(query));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CountDto>> Get(Guid id) =>
        Ok(await Mediator.Send(new GetCountQuery(id)));

    [HttpPost]
    public async Task<ActionResult<Guid>> Start(StartCountCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { id }, id);
    }

    /// <summary>One scan. Snapshots the system quantity as it stands right now — see CountLineCommand.</summary>
    [HttpPost("{id:guid}/lines")]
    public async Task<ActionResult<Guid>> CountLine(Guid id, CountLineCommand command) =>
        Ok(await Mediator.Send(command with { CountId = id }));

    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id)
    {
        await Mediator.Send(new SubmitCountCommand(id));
        return NoContent();
    }

    /// <summary>
    /// Approves the count and posts its variance as an adjustment. Returns the adjustment's id, or null
    /// when the shelf and the system agreed and there was nothing to post.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<Guid?>> Approve(Guid id) =>
        Ok(await Mediator.Send(new ApproveCountCommand(id)));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        await Mediator.Send(new CancelCountCommand(id));
        return NoContent();
    }
}

/// <summary>Stock reservations (requirements §20) — what "prevent overselling" is made of.</summary>
[Route("api/v1/inventory/reservations")]
public class StockReservationsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ReservationDto>>> List([FromQuery] GetReservationsQuery query) =>
        Ok(await Mediator.Send(query));

    /// <summary>Returns 422 if the stock is already promised to someone else.</summary>
    [HttpPost]
    public async Task<ActionResult<Guid>> Reserve(ReserveStockCommand command) =>
        Ok(await Mediator.Send(command));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Release(Guid id)
    {
        await Mediator.Send(new ReleaseReservationCommand(id));
        return NoContent();
    }

    /// <summary>
    /// Releases every reservation past its deadline. A scheduled job calls this; without it, a forgotten
    /// quote holds the last laptop off the shelf forever.
    /// </summary>
    [HttpPost("expire")]
    public async Task<ActionResult<int>> Expire() =>
        Ok(await Mediator.Send(new ExpireReservationsCommand()));
}

/// <summary>Serial numbers and their history (requirements §18).</summary>
[Route("api/v1/inventory/serials")]
public class SerialsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SerialDto>>> List([FromQuery] GetSerialsQuery query) =>
        Ok(await Mediator.Send(query));

    /// <summary>
    /// The query a warranty claim runs: scan the machine on the counter and see whether we sold it, when,
    /// under what warranty, and what has been done to it since.
    /// </summary>
    [HttpGet("{serialNumber}")]
    public async Task<ActionResult<SerialHistoryDto>> History(string serialNumber) =>
        Ok(await Mediator.Send(new GetSerialHistoryQuery(serialNumber)));
}

/// <summary>Barcode and QR label printing (requirements §17).</summary>
[Route("api/v1/inventory/labels")]
public class LabelsController : ApiControllerBase
{
    /// <summary>
    /// Renders a printable PDF. The server owns the geometry because a thermal printer needs exact page
    /// dimensions — see ILabelRenderer.
    /// </summary>
    [HttpPost("print")]
    public async Task<IActionResult> Print(PrintLabelsCommand command)
    {
        var result = await Mediator.Send(command);

        return File(result.Pdf, "application/pdf", result.FileName);
    }
}
