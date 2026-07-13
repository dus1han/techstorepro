using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Sales;

public enum SalesOrderStatus : short
{
    Draft = 1,

    /// <summary>Stock is reserved and the customer is committed.</summary>
    Confirmed = 2,

    PartiallyDelivered = 3,
    Delivered = 4,
    Cancelled = 5
}

/// <summary>
/// The customer has committed (requirements §22). <b>This is where stock is promised.</b>
///
/// Confirming an order reserves its lines through <c>IStockLedger.ReserveAsync</c>, which raises
/// <c>reserved_quantity</c> and so lowers what everyone else can sell. That — and nothing else — is what
/// "prevent overselling" means: two salespeople cannot both promise the last laptop, because the second
/// one's reservation fails against a balance the first already holds.
///
/// The goods have not moved yet. They move at <see cref="Delivery"/>.
/// </summary>
public class SalesOrder : TenantEntity
{
    public string Number { get; set; } = null!;

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    /// <summary>Where the stock is reserved from, and where it will be picked from.</summary>
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    /// <summary>Set when the order came from an accepted quotation.</summary>
    public Guid? QuotationId { get; set; }
    public Quotation? Quotation { get; set; }

    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Draft;

    /// <summary>Always the company's base currency — requirements §45 <b>D8</b>.</summary>
    public string CurrencyCode { get; set; } = "AED";

    public DateTimeOffset OrderedAt { get; set; }
    public DateTimeOffset? ExpectedAt { get; set; }

    public string? Notes { get; set; }

    public ICollection<SalesOrderLine> Lines { get; set; } = [];

    public decimal NetTotal => Lines.Sum(l => l.NetTotal);
    public decimal TaxTotal => Lines.Sum(l => l.TaxAmount);
    public decimal Total => NetTotal + TaxTotal;

    public bool IsFullyDelivered => Lines.All(l => l.OutstandingQuantity <= 0);

    public void Validate()
    {
        if (Lines.Count == 0)
        {
            throw new DomainException("A sales order with no lines sells nothing.");
        }
    }

    public void Confirm()
    {
        if (Status != SalesOrderStatus.Draft)
        {
            throw new DomainException($"An order that is {Status} cannot be confirmed.");
        }

        Validate();
        Status = SalesOrderStatus.Confirmed;
    }

    /// <summary>Called when a delivery is made against this order.</summary>
    public void RefreshDeliveryStatus()
    {
        if (Status is SalesOrderStatus.Draft or SalesOrderStatus.Cancelled)
        {
            return;
        }

        Status = IsFullyDelivered
            ? SalesOrderStatus.Delivered
            : Lines.Any(l => l.DeliveredQuantity > 0)
                ? SalesOrderStatus.PartiallyDelivered
                : SalesOrderStatus.Confirmed;
    }

    /// <summary>
    /// Cancelling releases the reservations — the caller's job, since only the ledger may touch them.
    /// Goods that have already left the building cannot be un-delivered by cancelling the paperwork;
    /// that is what a credit note is for.
    /// </summary>
    public void Cancel()
    {
        if (Lines.Any(l => l.DeliveredQuantity > 0))
        {
            throw new DomainException(
                "Part of this order has already been delivered. Cancelling it would leave stock off the "
                + "shelf with no document explaining where it went. Raise a credit note for the "
                + "delivered lines instead.");
        }

        Status = SalesOrderStatus.Cancelled;
    }
}

public class SalesOrderLine : TenantEntity
{
    public Guid SalesOrderId { get; set; }
    public SalesOrder SalesOrder { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string Description { get; set; } = null!;

    public decimal Quantity { get; set; }

    /// <summary>Ticked off as deliveries are made against this line.</summary>
    public decimal DeliveredQuantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }

    /// <summary>Snapshotted at order time. See <see cref="QuotationLine.TaxPercent"/>.</summary>
    public decimal TaxPercent { get; set; }

    public string? PriceSource { get; set; }

    /// <summary>
    /// The promise this line holds on the shelf, made at confirmation. The delivery hands this back to
    /// the ledger so that picking the goods consumes the reservation rather than competing with it —
    /// without it, delivering the two units you reserved would fail its own availability check.
    /// </summary>
    public Guid? StockReservationId { get; set; }

    public decimal OutstandingQuantity => Quantity - DeliveredQuantity;

    public decimal NetTotal => SalesMath.Net(Quantity, UnitPrice, DiscountPercent, DiscountAmount);
    public decimal TaxAmount => SalesMath.Tax(NetTotal, TaxPercent);
    public decimal LineTotal => NetTotal + TaxAmount;
}
