using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Purchasing;

public enum SupplierInvoiceStatus : short
{
    Draft = 1,
    Posted = 2,
    PartiallyPaid = 3,
    Paid = 4,
    Cancelled = 5
}

/// <summary>
/// What the supplier is asking to be paid (requirements §25).
///
/// It is deliberately a separate document from the goods receipt, even though the two usually carry the
/// same lines. They answer different questions and they can genuinely disagree: the goods arrive in
/// March and the invoice in April; three of the ten units are damaged and short-invoiced; the price on
/// the invoice is not the price on the order. A single document would force those disagreements to be
/// silently resolved in favour of whichever arrived last.
///
/// <b>It does not touch stock.</b> The goods receipt already did that. An invoice that moved stock as
/// well would double it.
/// </summary>
public class SupplierInvoice : TenantEntity
{
    public string Number { get; set; } = null!;

    /// <summary>The supplier's own invoice number. What they will quote when chasing payment.</summary>
    public string SupplierReference { get; set; } = null!;

    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    /// <summary>Null when the invoice arrived before the goods, or covers several receipts.</summary>
    public Guid? GoodsReceiptId { get; set; }
    public GoodsReceipt? GoodsReceipt { get; set; }

    public SupplierInvoiceStatus Status { get; set; } = SupplierInvoiceStatus.Draft;

    public string CurrencyCode { get; set; } = "AED";

    /// <summary>
    /// The rate on the invoice date. This fixes what the company <em>owes</em> in its own money — and it
    /// is the number the FX gain or loss is measured against when the payment finally goes out at a
    /// different rate. See <see cref="SupplierPayment"/>.
    /// </summary>
    public decimal ExchangeRate { get; set; } = 1m;

    public DateTimeOffset InvoicedAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }

    public string? Notes { get; set; }

    public ICollection<SupplierInvoiceLine> Lines { get; set; } = [];
    public ICollection<SupplierPaymentAllocation> Allocations { get; set; } = [];

    /// <summary>In the supplier's currency.</summary>
    public decimal Total => Lines.Sum(l => l.LineTotal);

    /// <summary>What the company owes, in its own money, fixed at the invoice rate.</summary>
    public decimal TotalBase => Total * ExchangeRate;

    /// <summary>Paid so far, in the invoice's currency — so it can be compared against <see cref="Total"/>.</summary>
    public decimal PaidAmount => Allocations.Sum(a => a.Amount);

    public decimal OutstandingAmount => Total - PaidAmount;

    public bool IsSettled => OutstandingAmount <= 0;

    public void Validate()
    {
        if (Lines.Count == 0)
        {
            throw new DomainException("A supplier invoice with no lines bills nothing.");
        }

        if (string.IsNullOrWhiteSpace(SupplierReference))
        {
            throw new DomainException(
                "A supplier invoice needs the supplier's own reference. Without it, nobody can match "
                + "this row to the piece of paper the supplier will chase you with.");
        }

        if (ExchangeRate <= 0)
        {
            throw new DomainException("An exchange rate must be greater than zero.");
        }
    }

    public void Post()
    {
        if (Status != SupplierInvoiceStatus.Draft)
        {
            throw new DomainException($"An invoice that is {Status} cannot be posted.");
        }

        Validate();

        Status = SupplierInvoiceStatus.Posted;
    }

    /// <summary>Called when a payment is allocated to, or removed from, this invoice.</summary>
    public void RefreshPaymentStatus()
    {
        if (Status is SupplierInvoiceStatus.Draft or SupplierInvoiceStatus.Cancelled)
        {
            return;
        }

        Status = IsSettled
            ? SupplierInvoiceStatus.Paid
            : PaidAmount > 0
                ? SupplierInvoiceStatus.PartiallyPaid
                : SupplierInvoiceStatus.Posted;
    }

    public void Cancel()
    {
        if (PaidAmount > 0)
        {
            throw new DomainException(
                "Money has already been paid against this invoice. Cancelling it would leave the "
                + "payment pointing at nothing. Raise a debit note instead.");
        }

        Status = SupplierInvoiceStatus.Cancelled;
    }
}

public class SupplierInvoiceLine : TenantEntity
{
    public Guid SupplierInvoiceId { get; set; }
    public SupplierInvoice SupplierInvoice { get; set; } = null!;

    /// <summary>Null for a line that is not a product at all — a delivery charge on the invoice itself.</summary>
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }

    public string Description { get; set; } = null!;

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal TaxPercent { get; set; }

    public decimal NetTotal => Quantity * UnitPrice * (1 - (DiscountPercent / 100m));

    public decimal TaxAmount => NetTotal * (TaxPercent / 100m);

    public decimal LineTotal => NetTotal + TaxAmount;
}
