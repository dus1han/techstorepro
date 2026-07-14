using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Sales;

/// <summary>How the money goes back (requirements §24).</summary>
public enum RefundMethod : short
{
    /// <summary>Cash out of the drawer.</summary>
    CashRefund = 1,

    BankRefund = 2,

    /// <summary>A credit on the account, to spend later. See <see cref="StoreCreditEntry"/>.</summary>
    StoreCredit = 3,

    /// <summary>
    /// Nothing is handed back: the credit simply reduces what the customer owes. The right answer when
    /// the invoice being credited has not been paid — refunding cash for money never received would hand
    /// the customer the shop's own money.
    /// </summary>
    OffsetAgainstBalance = 4
}

public enum CreditNoteStatus : short
{
    Issued = 1,
    Cancelled = 2
}

/// <summary>
/// The customer brought it back (requirements §24).
///
/// <b>A credit note is the only thing in sales that puts stock back.</b> It is not a cancelled invoice:
/// cancelling paperwork does not un-deliver goods, and an invoice that has been paid cannot be cancelled
/// at all. The goods physically returned, and the money physically goes back — those are two facts, and a
/// credit note records both.
///
/// The returned units go to <see cref="SerialStatus.Returned"/> and <b>not</b> straight back to
/// <c>InStock</c>. The serial state machine enforces that, and it is deliberate: a machine that came back
/// is inspected before it is sold to somebody else. Quantities alone cannot express "on the shelf but not
/// yet fit to sell", which is exactly why serials exist.
/// </summary>
public class CreditNote : TenantEntity
{
    public string Number { get; set; } = null!;

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    /// <summary>
    /// The invoice being credited. Required: requirements §24 asks for the invoice reference, and a
    /// credit note against nothing is money leaving the business with no explanation of what was sold.
    /// </summary>
    public Guid SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;

    /// <summary>Where the goods went back to. Null when nothing physical came back — a pricing correction.</summary>
    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public CreditNoteStatus Status { get; set; } = CreditNoteStatus.Issued;

    public RefundMethod RefundMethod { get; set; }

    public string CurrencyCode { get; set; } = "AED";

    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>Mandatory. Money is going back, and somebody will ask why.</summary>
    public string Reason { get; set; } = null!;

    public string? Notes { get; set; }

    public ICollection<CreditNoteLine> Lines { get; set; } = [];

    public decimal NetTotal => Lines.Sum(l => l.NetTotal);
    public decimal TaxTotal => Lines.Sum(l => l.TaxAmount);
    public decimal Total => NetTotal + TaxTotal;

    /// <summary>What the returned goods were worth to the shop — the COGS coming back onto the shelf.</summary>
    public decimal CostTotal => Lines.Sum(l => l.CostTotal);

    public void Validate()
    {
        if (Lines.Count == 0)
        {
            throw new DomainException("A credit note with no lines credits nothing.");
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            throw new DomainException(
                "A credit note needs a reason. Money is going back to the customer, and 'why' is the "
                + "first question anyone will ask of it.");
        }

        if (Lines.Any(l => l.Quantity <= 0))
        {
            throw new DomainException("A credit note line must credit a positive quantity.");
        }
    }
}

public class CreditNoteLine : TenantEntity
{
    public Guid CreditNoteId { get; set; }
    public CreditNote CreditNote { get; set; } = null!;

    /// <summary>
    /// The invoice line being credited. This is what makes a partial return possible — three of the ten
    /// cables came back — and what stops the same line being credited twice over.
    /// </summary>
    public Guid SalesInvoiceLineId { get; set; }
    public SalesInvoiceLine SalesInvoiceLine { get; set; } = null!;

    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }

    public string Description { get; set; } = null!;

    public decimal Quantity { get; set; }

    /// <summary>
    /// Copied from the invoice line, never re-resolved. The customer is refunded what they were charged —
    /// not what the price list says today, which may be lower (and would short-change them) or higher
    /// (and would hand them a profit for bringing something back).
    /// </summary>
    public decimal UnitPrice { get; set; }

    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }

    /// <summary>The rate the sale was taxed at, so the refund gives back exactly the tax that was charged.</summary>
    public decimal TaxPercent { get; set; }

    /// <summary>
    /// What the goods cost the shop when they left — carried back from the invoice line, so the stock
    /// returns to the shelf at the cost it left at rather than at today's moving average.
    /// </summary>
    public decimal UnitCost { get; set; }

    /// <summary>False when the goods did not come back — a price correction, or a write-off of faulty goods.</summary>
    public bool RestockedToShelf { get; set; } = true;

    public decimal NetTotal => SalesMath.Net(Quantity, UnitPrice, DiscountPercent, DiscountAmount);
    public decimal TaxAmount => SalesMath.Tax(NetTotal, TaxPercent);
    public decimal LineTotal => NetTotal + TaxAmount;

    public decimal CostTotal => SalesMath.Round(Quantity * UnitCost);
}
