using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Purchasing.GoodsReceipts;
using TechStorePro.Application.Purchasing.Imports;
using TechStorePro.Domain.Purchasing;
using Microsoft.AspNetCore.Mvc;

namespace TechStorePro.API.Controllers;

/// <summary>
/// Goods receipts (requirements §27) — the document that moves stock.
///
/// A receipt works <b>with or without</b> a purchase order. Requirements §25 makes the PO optional and
/// gives a direct-purchase flow, because a shop that drives to the wholesaler and comes back with a box
/// genuinely has no order. Forcing one would only produce fake orders raised after the fact, which look
/// real and are therefore worse than none.
/// </summary>
[Route("api/v1/goods-receipts")]
public class GoodsReceiptsController : ApiControllerBase
{
    /// <summary>
    /// Receives goods and posts them to stock, in one transaction. Serial numbers are captured here,
    /// at the door — which is what makes a warranty claim answerable two years later.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Guid>> Receive(ReceiveGoodsCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(Receive), new { id }, id);
    }
}

/// <summary>
/// Import shipments and landed cost (requirements §26).
///
/// The flow exists in this shape because <b>goods and their true cost do not arrive together</b>: the
/// container is unpacked in March and the clearing agent invoices in April.
///
/// <code>
/// POST /import-shipments              the container is on the water
/// POST /goods-receipts                it lands; stock posts at the goods price
/// POST /import-shipments/{id}/charges freight, duty, insurance, clearing — as they are billed
/// POST /import-shipments/{id}/apportion   fold that cost into the stock (decision D6: by value)
/// </code>
/// </summary>
[Route("api/v1/import-shipments")]
public class ImportShipmentsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ImportShipmentDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] ImportShipmentStatus? status = null) =>
        Ok(await Mediator.Send(new GetImportShipmentsQuery(page, pageSize, search, status)));

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateImportShipmentCommand command) =>
        Ok(await Mediator.Send(command));

    /// <summary>
    /// Bills the container for something that is not the goods — the shipping line, the insurer, the
    /// customs authority, the clearing agent. These arrive after the goods, which is the whole reason
    /// landed cost is a separate step from receiving.
    /// </summary>
    [HttpPost("{id:guid}/charges")]
    public async Task<ActionResult<Guid>> AddCharge(Guid id, AddImportChargeCommand command)
    {
        if (id != command.ShipmentId)
        {
            return BadRequest("Route id and body id differ.");
        }

        return Ok(await Mediator.Send(command));
    }

    /// <summary>
    /// <b>Folds the container's charges into the cost of its goods</b> (decision D6: by value).
    ///
    /// This is the single most consequential call in the purchasing module. Costing is weighted average
    /// (D1), so the money it moves does not merely price this container — it feeds the moving average of
    /// every product in it and spreads to units that arrived years ago, where it never washes out. It is
    /// gated on <c>Approve</c> rather than <c>Edit</c> for exactly that reason.
    ///
    /// It returns what was <em>absorbed</em> and what was not: if the container sold out before its
    /// clearing invoice arrived, there is no stock left to carry that cost, and the remainder is
    /// reported rather than silently dropped or smeared over the next shipment's goods.
    /// </summary>
    [HttpPost("{id:guid}/apportion")]
    public async Task<ActionResult<ApportionmentResultDto>> Apportion(
        Guid id,
        [FromQuery] ApportionmentBasis basis = ApportionmentBasis.ByValue) =>
        Ok(await Mediator.Send(new ApportionLandedCostCommand(id, basis)));
}
