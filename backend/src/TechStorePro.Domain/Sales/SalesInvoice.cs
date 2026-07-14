using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Sales;

public enum SalesInvoiceStatus : short
{
    Draft = 1,

    /// <summary>Issued to the customer. It is now money they owe — <c>Customer.Balance</c> says so.</summary>
    Posted = 2,

    PartiallyPaid = 3,
    Paid = 4,
    Cancelled = 5
}

/// <summary>
/// The bill (requirements §22). <b>It is what the customer owes, and it does not move stock.</b>
///
/// The stock left at <see cref="Delivery"/>. An invoice that moved stock as well would double the
/// issue — the mirror image of the rule that a supplier invoice does not move stock because the goods
/// receipt already did.
///
/// The one exception is the counter sale, where there is no separate delivery document because the
/// customer walks out with the goods: the handler posts the movement and the delivery in the same
/// breath. Even then it is the <em>delivery</em> that moves the stock, not the invoice — the invoice
/// only ever reads the cost the ledger came back with.
/// </summary>
public class SalesInvoice : TenantEntity
{
    public string Number { get; set; } = null!;

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public Guid? SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }

    /// <summary>The goods this bill is for. Null only for a service-only invoice — no goods, no delivery.</summary>
    public Guid? DeliveryId { get; set; }
    public Delivery? Delivery { get; set; }

    public SalesInvoiceStatus Status { get; set; } = SalesInvoiceStatus.Draft;

    /// <summary>
    /// Always the company's base currency (requirements §45 <b>D8</b>). It is stored rather than assumed
    /// so that a printed invoice from 2026 still says what money it was in, even if the company later
    /// changes its base currency.
    /// </summary>
    public string CurrencyCode { get; set; } = "AED";

    public DateTimeOffset InvoicedAt { get; set; }

    /// <summary>Driven by the customer's payment terms. Null = due on receipt.</summary>
    public DateTimeOffset? DueAt { get; set; }

    public string? Notes { get; set; }

    public ICollection<SalesInvoiceLine> Lines { get; set; } = [];
    public ICollection<CustomerPaymentAllocation> Allocations { get; set; } = [];

    public decimal NetTotal => Lines.Sum(l => l.NetTotal);
    public decimal TaxTotal => Lines.Sum(l => l.TaxAmount);
    public decimal Total => NetTotal + TaxTotal;

    /// <summary>Received against this invoice — the sum of what payments have pointed at it.</summary>
    public decimal PaidAmount => Allocations.Sum(a => a.Amount);

    public decimal OutstandingAmount => Total - PaidAmount;

    public bool IsSettled => OutstandingAmount <= 0;

    /// <summary>What the goods on this invoice cost the shop. Revenue − this is the margin (§45 D3).</summary>
    public decimal CostTotal => Lines.Sum(l => l.CostTotal);

    public decimal GrossProfit => NetTotal - CostTotal;

    public void Validate()
    {
        if (Lines.Count == 0)
        {
            throw new DomainException("An invoice with no lines bills nothing.");
        }

        if (Lines.Any(l => l.Quantity <= 0))
        {
            throw new DomainException("An invoice line must bill a positive quantity.");
        }
    }

    /// <summary>
    /// Issues the invoice. The caller raises <c>Customer.Balance</c> by <see cref="Total"/> in the same
    /// transaction — posting the bill and recording the debt are one act, not two.
    /// </summary>
    public void Post()
    {
        if (Status != SalesInvoiceStatus.Draft)
        {
            throw new DomainException($"An invoice that is {Status} cannot be posted.");
        }

        Validate();
        Status = SalesInvoiceStatus.Posted;
    }

    /// <summary>
    /// Called when money is allocated to, or removed from, this invoice. The status is derived from the
    /// allocations rather than set by the caller — an invoice marked Paid that nobody paid is exactly the
    /// kind of lie a receivables report cannot survive.
    /// </summary>
    public void RefreshPaymentStatus()
    {
        if (Status is SalesInvoiceStatus.Draft or SalesInvoiceStatus.Cancelled)
        {
            return;
        }

        Status = IsSettled
            ? SalesInvoiceStatus.Paid
            : PaidAmount > 0
                ? SalesInvoiceStatus.PartiallyPaid
                : SalesInvoiceStatus.Posted;
    }

    public void Cancel()
    {
        if (Status is SalesInvoiceStatus.PartiallyPaid or SalesInvoiceStatus.Paid)
        {
            throw new DomainException(
                "Money has already been received against this invoice. Cancelling it would leave the "
                + "payment pointing at nothing. Raise a credit note instead.");
        }

        Status = SalesInvoiceStatus.Cancelled;
    }
}

public class SalesInvoiceLine : TenantEntity
{
    public Guid SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;

    /// <summary>Which picked goods this line bills. Null for a service or a charge line.</summary>
    public Guid? DeliveryLineId { get; set; }
    public DeliveryLine? DeliveryLine { get; set; }

    /// <summary>Null for a line that is not a product at all — a delivery charge, a callout fee.</summary>
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }

    public string Description { get; set; } = null!;

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// The rate in force when the invoice was raised — snapshotted, never an FK (see
    /// <see cref="TaxRate"/>). This is why <c>POST /tax-rates/{id}/supersede</c> exists rather than an
    /// edit: an invoice issued at 5% must still read 5% after the rate changes.
    /// </summary>
    public decimal TaxPercent { get; set; }

    public string? PriceSource { get; set; }

    /// <summary>
    /// COGS for this line — the ledger's valuation at the moment the stock moved, carried over from the
    /// delivery. Zero for a service line, which consumes no stock.
    /// </summary>
    public decimal UnitCost { get; set; }

    /// <summary>Set when the discount on this line exceeded its ceiling and a manager signed for it (§32).</summary>
    public Guid? DiscountApprovedBy { get; set; }

    public decimal NetTotal => SalesMath.Net(Quantity, UnitPrice, DiscountPercent, DiscountAmount);
    public decimal TaxAmount => SalesMath.Tax(NetTotal, TaxPercent);
    public decimal LineTotal => NetTotal + TaxAmount;

    public decimal CostTotal => SalesMath.Round(Quantity * UnitCost);

    /// <summary>Margin on this line, before tax — tax is the government's money, not the shop's.</summary>
    public decimal GrossProfit => NetTotal - CostTotal;
}
