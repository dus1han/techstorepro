using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Sales;

public enum DeliveryStatus : short
{
    /// <summary>The goods have left the warehouse. Stock has moved; the ledger says so.</summary>
    Delivered = 1,

    Invoiced = 2,

    /// <summary>Reversed by a credit note (P5 slice 3), which is what puts the goods back.</summary>
    Cancelled = 3
}

/// <summary>
/// The goods physically leave (requirements §22). <b>This is what takes them off the shelf</b> — a
/// <c>MovementType.Sale</c> through <c>IStockLedger</c>, and the only outbound path sales has.
///
/// It is a separate document from the invoice on purpose, and the reason is the serial. A delivery is
/// where a specific laptop — this one, with this serial, not just "a laptop" — is picked and handed
/// over. The invoice can be raised before the goods go (a proforma), after them (monthly billing), or
/// cover three deliveries at once; none of that changes which machine went out of the door.
///
/// <b>Serial binding here is what makes a warranty claim answerable two years later</b> (P6). Deferring
/// it to the invoice would mean a shop that invoices monthly cannot say which unit it sold.
/// </summary>
public class Delivery : TenantEntity
{
    public string Number { get; set; } = null!;

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    /// <summary>
    /// Null for a counter sale — a walk-in who takes the goods there and then. Requiring an order for
    /// every sale would produce fakes raised after the fact, exactly as requiring a PO for every
    /// receipt would (§25, and the same reasoning).
    /// </summary>
    public Guid? SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }

    public DeliveryStatus Status { get; set; } = DeliveryStatus.Delivered;

    public DateTimeOffset DeliveredAt { get; set; }

    public string? DeliveredTo { get; set; }
    public string? Notes { get; set; }

    public ICollection<DeliveryLine> Lines { get; set; } = [];

    /// <summary>What the goods cost the shop — the moving average at the moment they left.</summary>
    public decimal CostTotal => Lines.Sum(l => l.CostTotal);

    public void Validate()
    {
        if (Lines.Count == 0)
        {
            throw new DomainException("A delivery with no lines delivers nothing.");
        }
    }
}

public class DeliveryLine : TenantEntity
{
    public Guid DeliveryId { get; set; }
    public Delivery Delivery { get; set; } = null!;

    /// <summary>Null on a counter sale, which has no order to tick off.</summary>
    public Guid? SalesOrderLineId { get; set; }
    public SalesOrderLine? SalesOrderLine { get; set; }

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal Quantity { get; set; }

    /// <summary>
    /// <b>COGS, snapshotted.</b> What the ledger actually valued this issue at — the warehouse's weighted
    /// average at the instant the stock moved (§45 D1).
    ///
    /// It is stored rather than recomputed because the average <em>moves</em>: the next receipt at a
    /// different price changes it, and a margin report that recomputed cost later would quietly restate
    /// the profit on every sale the shop has ever made.
    /// </summary>
    public decimal UnitCost { get; set; }

    public string? Notes { get; set; }

    public ICollection<DeliverySerial> Serials { get; set; } = [];

    public decimal CostTotal => SalesMath.Round(Quantity * UnitCost);
}

/// <summary>
/// Which machine went out of the door. One row per unit — that is what makes it a serial and not a
/// quantity.
/// </summary>
public class DeliverySerial : TenantEntity
{
    public Guid DeliveryLineId { get; set; }
    public DeliveryLine DeliveryLine { get; set; } = null!;

    public string SerialNumber { get; set; } = null!;

    public Guid SerialId { get; set; }
}
