using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Purchasing;

/// <summary>
/// Goods have physically arrived (requirements §27). <b>This is the document that moves stock.</b>
///
/// <see cref="PurchaseOrderId"/> is <b>nullable, deliberately</b>. Requirements §25 makes the PO
/// optional and gives a direct-purchase flow — supplier → GRN → stock → invoice — because a shop that
/// drives to the wholesaler and comes back with a box genuinely has no purchase order. Forcing one
/// would make the staff raise fake orders after the fact, which is worse than having none: the fakes
/// look real.
///
/// <para><b>It posts at the cost it knows.</b> For a local purchase that is the supplier's price and it
/// is final. For an import it is only the goods price — the freight, duty and clearing may not have
/// been invoiced yet, and the shop cannot refuse to book stock it can see on the shelf. So the receipt
/// posts, and the landed cost is folded in afterwards by <see cref="ImportShipment"/> as a
/// revaluation. See <c>MovementType.Revaluation</c>.</para>
/// </summary>
public class GoodsReceipt : TenantEntity
{
    public string Number { get; set; } = null!;

    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    /// <summary>Null for a direct purchase — see the class remarks. This is the whole of §25's flexibility.</summary>
    public Guid? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    /// <summary>Set when the goods came in a container whose charges will be apportioned later (§26).</summary>
    public Guid? ImportShipmentId { get; set; }
    public ImportShipment? ImportShipment { get; set; }

    public string CurrencyCode { get; set; } = "AED";

    /// <summary>
    /// The rate on the day the goods landed. This — not the order's rate — is what values the stock,
    /// because this is the day the company took ownership of it.
    /// </summary>
    public decimal ExchangeRate { get; set; } = 1m;

    /// <summary>The supplier's delivery note number, so a query can be answered without ringing them.</summary>
    public string? SupplierReference { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public string? Notes { get; set; }

    public ICollection<GoodsReceiptLine> Lines { get; set; } = [];

    /// <summary>Goods value in the supplier's currency, before any landed cost.</summary>
    public decimal GoodsTotal => Lines.Sum(l => l.LineTotal);

    /// <summary>Goods value in the company's base currency. This is what hit the ledger.</summary>
    public decimal GoodsTotalBase => GoodsTotal * ExchangeRate;

    public void Validate()
    {
        if (Lines.Count == 0)
        {
            throw new DomainException("A goods receipt with no lines receives nothing.");
        }

        if (Lines.Any(l => l.Quantity <= 0))
        {
            throw new DomainException("A goods receipt line must receive at least one unit.");
        }

        if (ExchangeRate <= 0)
        {
            throw new DomainException("An exchange rate must be greater than zero.");
        }
    }
}

public class GoodsReceiptLine : TenantEntity
{
    public Guid GoodsReceiptId { get; set; }
    public GoodsReceipt GoodsReceipt { get; set; } = null!;

    /// <summary>Null on a direct purchase, or on a line that was not on the order at all.</summary>
    public Guid? PurchaseOrderLineId { get; set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal Quantity { get; set; }

    /// <summary>What the supplier charged, in the receipt's currency. Not the landed cost.</summary>
    public decimal UnitPrice { get; set; }

    public decimal DiscountPercent { get; set; }

    /// <summary>
    /// The landed cost apportioned to this line, in base currency, once the shipment's charges are
    /// known. Zero for a local purchase — nothing was shipped, so nothing was apportioned.
    /// </summary>
    public decimal ApportionedCost { get; set; }

    public string? Notes { get; set; }

    public decimal LineTotal => Quantity * UnitPrice * (1 - (DiscountPercent / 100m));

    /// <summary>
    /// What the ledger booked this line at, per unit, in base currency: the goods price plus its share
    /// of the shipment's charges.
    ///
    /// <b>This is the number D1 warned about.</b> It is what feeds the moving average, so getting the
    /// apportionment wrong does not just misprice this container — it spreads to every existing unit of
    /// the product and never washes out.
    /// </summary>
    public decimal LandedUnitCost =>
        Quantity == 0
            ? 0
            : ((LineTotal * GoodsReceipt.ExchangeRate) + ApportionedCost) / Quantity;

    /// <summary>Serial numbers captured at the door, for a serial-tracked product (requirements §27).</summary>
    public ICollection<GoodsReceiptSerial> Serials { get; set; } = [];
}

/// <summary>
/// One machine, captured as it came out of the box.
///
/// Capturing serials at receipt rather than at sale is what makes P6's warranty flow answerable: the
/// serial ties the laptop on the counter back to the container it arrived in, the supplier who sent it,
/// and what it actually cost.
/// </summary>
public class GoodsReceiptSerial : TenantEntity
{
    public Guid GoodsReceiptLineId { get; set; }
    public GoodsReceiptLine GoodsReceiptLine { get; set; } = null!;

    public string SerialNumber { get; set; } = null!;

    /// <summary>The serial row the ledger created or moved. Set once the receipt has posted.</summary>
    public Guid? SerialId { get; set; }
}
