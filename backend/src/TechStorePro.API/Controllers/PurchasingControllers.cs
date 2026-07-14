using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Purchasing.GoodsReceipts;
using TechStorePro.Application.Purchasing.Imports;
using TechStorePro.Application.Purchasing.Invoices;
using TechStorePro.Application.Purchasing.Orders;
using TechStorePro.Application.Purchasing.Payments;
using TechStorePro.Application.Purchasing.Queries;
using TechStorePro.Domain.Purchasing;
using Microsoft.AspNetCore.Mvc;

namespace TechStorePro.API.Controllers;

/// <summary>
/// Purchase orders (requirements §25) — <b>optional</b>, and the system means it.
///
/// A shop that drives to the wholesaler and comes back with a box has no order and never will. The
/// direct purchase (supplier → GRN → stock) is a first-class path, so nothing here is on the critical
/// route to receiving goods. What the order is for is the case where it earns its keep: committing to a
/// price and a quantity before the goods exist, so what arrives can be checked against what was agreed.
/// </summary>
[Route("api/v1/purchase-orders")]
public class PurchaseOrdersController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<PurchaseOrderDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] PurchaseOrderStatus? status = null,
        [FromQuery] Guid? supplierId = null) =>
        Ok(await Mediator.Send(new GetPurchaseOrdersQuery(page, pageSize, search, status, supplierId)));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PurchaseOrderDto>> Get(Guid id) =>
        Ok(await Mediator.Send(new GetPurchaseOrderByIdQuery(id)));

    /// <summary>Raises a draft. Nothing is committed until it is approved.</summary>
    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreatePurchaseOrderCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { id }, id);
    }

    /// <summary>
    /// <b>Commits the company's money</b> — which is why it is a separate permission from creating one,
    /// so the person who chooses the supplier need not be the person who signs for it. It is also the
    /// gate on receiving: goods cannot be posted against a draft order.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id)
    {
        await Mediator.Send(new ApprovePurchaseOrderCommand(id));
        return NoContent();
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancelPurchaseOrderCommand command)
    {
        if (id != command.PurchaseOrderId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }
}

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
    [HttpGet]
    public async Task<ActionResult<PagedResult<GoodsReceiptDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] Guid? supplierId = null,
        [FromQuery] Guid? importShipmentId = null) =>
        Ok(await Mediator.Send(new GetGoodsReceiptsQuery(page, pageSize, search, supplierId, importShipmentId)));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GoodsReceiptDto>> Get(Guid id) =>
        Ok(await Mediator.Send(new GetGoodsReceiptByIdQuery(id)));

    /// <summary>
    /// Receives goods and posts them to stock, in one transaction. Serial numbers are captured here,
    /// at the door — which is what makes a warranty claim answerable two years later.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Guid>> Receive(ReceiveGoodsCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { id }, id);
    }
}

/// <summary>
/// Supplier invoices (requirements §25) — what the supplier is asking to be paid.
///
/// <b>Deliberately separate from the goods receipt, and it does not touch stock.</b> The receipt already
/// moved it; an invoice that moved stock as well would double it. The two documents can genuinely
/// disagree — the goods arrive in March and the invoice in April, three of ten units are short-invoiced,
/// the price billed is not the price ordered — and a single document would force those disagreements to
/// be resolved silently in favour of whichever arrived last.
/// </summary>
[Route("api/v1/supplier-invoices")]
public class SupplierInvoicesController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SupplierInvoiceDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] SupplierInvoiceStatus? status = null,
        [FromQuery] Guid? supplierId = null) =>
        Ok(await Mediator.Send(new GetSupplierInvoicesQuery(page, pageSize, search, status, supplierId)));

    [HttpPost]
    public async Task<ActionResult<Guid>> Record(RecordSupplierInvoiceCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(Record), new { id }, id);
    }

    /// <summary>Posting is what puts the debt on the supplier's balance. A draft owes nothing.</summary>
    [HttpPost("{id:guid}/post")]
    public async Task<IActionResult> Post(Guid id)
    {
        await Mediator.Send(new PostSupplierInvoiceCommand(id));
        return NoContent();
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancelSupplierInvoiceCommand command)
    {
        if (id != command.SupplierInvoiceId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }
}

/// <summary>
/// Money paid to a supplier (requirements §25) — a header plus allocations, not a column on an invoice.
///
/// One transfer settles three invoices; one invoice is settled by two instalments; a payment may settle
/// nothing at all and sit as an advance. A single <c>invoice_id</c> expresses none of those, and a shop
/// that pays its supplier monthly does all three.
///
/// <b>This is also where a foreign-currency purchase settles up with reality.</b> The invoice fixed the
/// debt in base currency at the invoice-date rate; the money leaves the bank at the payment-date rate;
/// the difference is a realised FX gain or loss that the business made by owing money in a currency it
/// does not hold — and it belongs in the P&amp;L, not in the cost of the stock (§26, D1).
/// </summary>
[Route("api/v1/supplier-payments")]
public class SupplierPaymentsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SupplierPaymentDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] Guid? supplierId = null) =>
        Ok(await Mediator.Send(new GetSupplierPaymentsQuery(page, pageSize, search, supplierId)));

    [HttpPost]
    public async Task<ActionResult<Guid>> Pay(PaySupplierCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(Pay), new { id }, id);
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
