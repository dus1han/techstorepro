using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Sales.Deliveries;
using TechStorePro.Application.Sales.Invoices;
using TechStorePro.Application.Sales.Orders;
using TechStorePro.Application.Sales.Payments;
using TechStorePro.Application.Sales.Pos;
using TechStorePro.Application.Sales.Queries;
using TechStorePro.Application.Sales.Quotations;
using TechStorePro.Domain.Sales;
using Microsoft.AspNetCore.Mvc;

namespace TechStorePro.API.Controllers;

/// <summary>
/// Quotations (requirements §22) — a price, promised for a while.
///
/// A quotation <b>reserves nothing</b>. Quoting ten laptops the shop does not have is legitimate, and
/// holding stock for every speculative quote would empty the warehouse on paper while it sat full.
/// </summary>
[Route("api/v1/quotations")]
public class QuotationsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<QuotationDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] QuotationStatus? status = null,
        [FromQuery] Guid? customerId = null) =>
        Ok(await Mediator.Send(new GetQuotationsQuery(page, pageSize, search, status, customerId)));

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateQuotationCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(Create), new { id }, id);
    }

    [HttpPost("{id:guid}/send")]
    public async Task<IActionResult> Send(Guid id)
    {
        await Mediator.Send(new SendQuotationCommand(id));
        return NoContent();
    }

    [HttpPost("{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id)
    {
        await Mediator.Send(new AcceptQuotationCommand(id));
        return NoContent();
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, RejectQuotationCommand command)
    {
        if (id != command.QuotationId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    /// <summary>
    /// Turns the quotation into a sales order, at the price that was quoted. The promised price does not
    /// re-resolve against today's list — that promise is what a quotation is.
    /// </summary>
    [HttpPost("{id:guid}/convert")]
    public async Task<ActionResult<Guid>> Convert(Guid id, ConvertQuotationCommand command)
    {
        if (id != command.QuotationId)
        {
            return BadRequest("Route id and body id differ.");
        }

        return Ok(await Mediator.Send(command));
    }
}

/// <summary>
/// Sales orders (requirements §22). <b>Confirming one is what promises the stock</b> — it reserves every
/// line through the ledger, and that reservation is the whole of "prevent overselling".
/// </summary>
[Route("api/v1/sales-orders")]
public class SalesOrdersController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SalesOrderDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] SalesOrderStatus? status = null,
        [FromQuery] Guid? customerId = null) =>
        Ok(await Mediator.Send(new GetSalesOrdersQuery(page, pageSize, search, status, customerId)));

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateSalesOrderCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(Create), new { id }, id);
    }

    /// <summary>
    /// Reserves the stock and checks the customer's credit limit. Both belong here: this is the moment
    /// the shop commits goods to someone who has not paid.
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id)
    {
        await Mediator.Send(new ConfirmSalesOrderCommand(id));
        return NoContent();
    }

    /// <summary>Cancels the order and gives the reserved stock back to the shelf.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancelSalesOrderCommand command)
    {
        if (id != command.SalesOrderId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }
}

/// <summary>
/// Deliveries (requirements §22) — <b>the only thing in sales that moves stock.</b>
///
/// A delivery works with or without a sales order: the counter sale hands the goods over on the spot and
/// there is no order to raise first. Serial numbers are bound here, at the door, which is what lets P6
/// answer a warranty claim two years later.
/// </summary>
[Route("api/v1/deliveries")]
public class DeliveriesController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<DeliveryDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] DeliveryStatus? status = null,
        [FromQuery] Guid? customerId = null) =>
        Ok(await Mediator.Send(new GetDeliveriesQuery(page, pageSize, search, status, customerId)));

    [HttpPost]
    public async Task<ActionResult<Guid>> Deliver(DeliverGoodsCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(Deliver), new { id }, id);
    }
}

/// <summary>
/// Sales invoices (requirements §22) — what the customer owes.
///
/// The invoice <b>does not move stock</b>: the delivery already did, and an invoice that moved it too
/// would issue the same laptop twice. What it does is price the delivered goods, snapshot the tax rate
/// and the COGS the ledger came back with, raise the customer's balance, and bind each delivered serial
/// to the line that sold it.
/// </summary>
[Route("api/v1/sales-invoices")]
public class SalesInvoicesController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SalesInvoiceDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] SalesInvoiceStatus? status = null,
        [FromQuery] Guid? customerId = null) =>
        Ok(await Mediator.Send(new GetInvoicesQuery(page, pageSize, search, status, customerId)));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SalesInvoiceDto>> Get(Guid id) =>
        Ok(await Mediator.Send(new GetInvoiceByIdQuery(id)));

    [HttpPost]
    public async Task<ActionResult<Guid>> Raise(RaiseInvoiceCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(Get), new { id }, id);
    }

    /// <summary>
    /// Cancels an unpaid invoice and takes the debt back off the customer. It does <b>not</b> return the
    /// stock — the goods left at the delivery, and getting them back is a credit note.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancelInvoiceCommand command)
    {
        if (id != command.InvoiceId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }
}

/// <summary>
/// Customer payments (requirements §23).
///
/// A payment is a <b>header, its tender lines and its allocations</b>. One sale is settled by cash
/// <em>and</em> card; one bank transfer settles three invoices; one invoice is settled by two instalments.
/// A single <c>payment_method_id</c> and a single <c>invoice_id</c> on the header can express none of
/// that.
///
/// Money may arrive with no allocation at all — a deposit, or a customer paying down their account. It is
/// not lost and not guessed at: it sits as a credit and takes their balance negative.
///
/// <b>Send an <c>Idempotency-Key</c> header.</b> A double-clicked "Take payment" would otherwise take the
/// money twice.
/// </summary>
[Route("api/v1/customer-payments")]
public class CustomerPaymentsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerPaymentDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] Guid? customerId = null) =>
        Ok(await Mediator.Send(new GetPaymentsQuery(page, pageSize, search, customerId)));

    [HttpPost]
    public async Task<ActionResult<Guid>> Record(RecordPaymentCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(Record), new { id }, id);
    }
}

/// <summary>
/// The till (requirements §22, "POS sales").
///
/// <b>One call, one transaction, three documents</b> — the goods leave, the bill is raised, the money is
/// taken. At a counter those are a single act: a customer who has walked out with a laptop has not
/// "maybe" paid for it. If the card is declined, the laptop is still in stock and no invoice is chasing
/// anybody.
///
/// It composes the same handlers the documented flow uses, so there is still exactly one path by which
/// stock moves. <b>Send an <c>Idempotency-Key</c>.</b>
/// </summary>
[Route("api/v1/pos")]
public class PosController : ApiControllerBase
{
    [HttpPost("sales")]
    public async Task<ActionResult<CounterSaleResult>> Sell(SellAtCounterCommand command) =>
        Ok(await Mediator.Send(command));
}
