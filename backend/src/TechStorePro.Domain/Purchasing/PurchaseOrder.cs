using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Purchasing;

public enum PurchaseOrderStatus : short
{
    Draft = 1,
    Approved = 2,

    /// <summary>Some of it has arrived, some has not.</summary>
    PartiallyReceived = 3,

    Received = 4,
    Cancelled = 5
}

/// <summary>
/// An order placed with a supplier — and <b>optional</b>, which is the whole point (requirements §25:
/// "PR is not required. PO is optional.").
///
/// A shop that walks to the wholesaler and comes back with a box has no purchase order and never will.
/// Requiring one would make the system lie about how the business actually buys, and the staff would
/// route around it by raising fake orders after the fact — which is worse than not having them, because
/// then the orders look real. So <see cref="GoodsReceipt.PurchaseOrderId"/> is nullable and the direct
/// flow (supplier → GRN → invoice) is a first-class path, not a workaround.
///
/// What the PO is for is the case where it earns its keep: committing to a price and a quantity before
/// the goods exist, so that what arrives can be checked against what was agreed.
/// </summary>
public class PurchaseOrder : TenantEntity
{
    public string Number { get; set; } = null!;

    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    /// <summary>Where the goods are expected to land. Chosen up front so the GRN has a default.</summary>
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

    /// <summary>
    /// The currency the supplier bills in — not necessarily the company's. An overseas supplier
    /// invoices in USD and the shop's books are in AED (requirements §26).
    /// </summary>
    public string CurrencyCode { get; set; } = "AED";

    /// <summary>
    /// The rate agreed when the order was placed, held so the order's value in base currency is stable.
    /// The <em>receipt</em> takes its own rate: the money actually moves on the day the goods land, and
    /// the difference between the two is a real FX gain or loss, not a rounding error to be hidden.
    /// </summary>
    public decimal ExchangeRate { get; set; } = 1m;

    public DateTimeOffset OrderedAt { get; set; }
    public DateTimeOffset? ExpectedAt { get; set; }

    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    public string? Notes { get; set; }

    public ICollection<PurchaseOrderLine> Lines { get; set; } = [];

    /// <summary>In the supplier's currency. Base-currency value is this × <see cref="ExchangeRate"/>.</summary>
    public decimal Total => Lines.Sum(l => l.LineTotal);

    public bool IsFullyReceived => Lines.Count > 0 && Lines.All(l => l.ReceivedQuantity >= l.Quantity);

    public void Validate()
    {
        if (Lines.Count == 0)
        {
            throw new DomainException("A purchase order with no lines orders nothing.");
        }

        if (ExchangeRate <= 0)
        {
            throw new DomainException("An exchange rate must be greater than zero.");
        }
    }

    public void Approve(Guid? by, DateTimeOffset at)
    {
        if (Status != PurchaseOrderStatus.Draft)
        {
            throw new DomainException($"A purchase order that is {Status} cannot be approved.");
        }

        Validate();

        Status = PurchaseOrderStatus.Approved;
        ApprovedBy = by;
        ApprovedAt = at;
    }

    /// <summary>Called by the goods receipt as stock arrives against this order.</summary>
    public void RefreshReceiptStatus()
    {
        if (Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Draft)
        {
            return;
        }

        Status = IsFullyReceived
            ? PurchaseOrderStatus.Received
            : Lines.Any(l => l.ReceivedQuantity > 0)
                ? PurchaseOrderStatus.PartiallyReceived
                : PurchaseOrderStatus.Approved;
    }

    public void Cancel()
    {
        // Once goods have arrived against it, the order is a historical fact — the stock is on the
        // shelf and the supplier will invoice for it. Cancelling would leave a receipt pointing at an
        // order that claims it never happened.
        if (Lines.Any(l => l.ReceivedQuantity > 0))
        {
            throw new DomainException(
                "Goods have already been received against this order, so it cannot be cancelled. "
                + "Return them to the supplier instead.");
        }

        Status = PurchaseOrderStatus.Cancelled;
    }
}

public class PurchaseOrderLine : TenantEntity
{
    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal Quantity { get; set; }

    /// <summary>Agreed with the supplier, in the order's currency. Not yet a landed cost.</summary>
    public decimal UnitPrice { get; set; }

    public decimal DiscountPercent { get; set; }

    /// <summary>How much of this line has actually turned up. Maintained by the goods receipt.</summary>
    public decimal ReceivedQuantity { get; set; }

    public string? Notes { get; set; }

    public decimal LineTotal => Quantity * UnitPrice * (1 - (DiscountPercent / 100m));

    public decimal OutstandingQuantity => Math.Max(0, Quantity - ReceivedQuantity);
}
