using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Inventory;

/// <summary>Where one physical machine is in its life (requirements §18).</summary>
public enum SerialStatus : short
{
    /// <summary>On the shelf, sellable.</summary>
    InStock = 1,

    /// <summary>Promised to a quote or an order. Still physically here; no longer sellable.</summary>
    Reserved = 2,

    /// <summary>Between warehouses. Deliberately owned by neither, so it cannot be sold twice.</summary>
    InTransit = 3,

    Sold = 4,

    /// <summary>In the workshop — ours, under repair, or consumed by a repair job.</summary>
    InRepair = 5,

    /// <summary>Came back from a customer and has not been put back on the shelf yet.</summary>
    Returned = 6,

    /// <summary>Written off. Broken, lost, or a count found it missing.</summary>
    Scrapped = 7,

    /// <summary>
    /// Sent back to the supplier. Terminal, and deliberately distinct from <see cref="Scrapped"/>:
    /// one is a loss the business absorbs, the other is a credit it is owed, and a write-off report
    /// that confuses them overstates the loss.
    /// </summary>
    ReturnedToSupplier = 8
}

/// <summary>
/// One physical unit of a serial-tracked product — a laptop, a monitor, a printer (requirements §18).
///
/// This row is what makes a warranty claim answerable two years later: the serial ties the machine on
/// the counter to the receipt that brought it in, the customer who bought it, and every repair since.
/// <see cref="SerialEvent"/> is that history, and it is why status is not enough on its own.
/// </summary>
public class Serial : TenantEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string SerialNumber { get; set; } = null!;

    public SerialStatus Status { get; set; } = SerialStatus.InStock;

    /// <summary>Null once it has left us — sold, scrapped, or in transit between warehouses.</summary>
    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    /// <summary>
    /// What this exact unit cost — the landed cost of the unit itself, not the product's moving
    /// average. A serial-tracked machine's true margin is knowable, so it is kept.
    /// </summary>
    public decimal PurchaseCost { get; set; }

    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>Set by the goods receipt that brought it in (P4).</summary>
    public Guid? GoodsReceiptLineId { get; set; }

    /// <summary>Set by the sale that put it in a customer's hands (P5). The warranty flow reads this.</summary>
    public Guid? SoldInvoiceLineId { get; set; }

    /// <summary>The end of the shop warranty, computed from the product's warranty months at sale.</summary>
    public DateTimeOffset? WarrantyUntil { get; set; }

    public ICollection<SerialEvent> Events { get; set; } = [];

    public bool IsUnderWarrantyAt(DateTimeOffset at) => WarrantyUntil is { } until && until > at;

    /// <summary>
    /// Moves the unit to a new status, refusing the transitions that would corrupt the ledger.
    ///
    /// The rule that matters: <b>a unit that is not in stock cannot be issued</b>. Without it, the same
    /// laptop could be sold twice — once for real, once because a second sale never checked — and the
    /// balance would still look right, because quantities and serials would have drifted apart.
    /// </summary>
    public void TransitionTo(SerialStatus next, Guid? warehouseId)
    {
        var allowed = Status switch
        {
            SerialStatus.InStock => next is SerialStatus.Reserved or SerialStatus.Sold or SerialStatus.InTransit
                or SerialStatus.InRepair or SerialStatus.Scrapped or SerialStatus.ReturnedToSupplier,

            SerialStatus.Reserved => next is SerialStatus.InStock or SerialStatus.Sold or SerialStatus.Scrapped,

            SerialStatus.InTransit => next is SerialStatus.InStock or SerialStatus.Scrapped,

            // A sold unit comes back as Returned (a return) or InRepair (a warranty claim). It never
            // silently reappears as InStock: someone has to look at it and decide it is resaleable.
            SerialStatus.Sold => next is SerialStatus.Returned or SerialStatus.InRepair,

            SerialStatus.InRepair => next is SerialStatus.InStock or SerialStatus.Sold
                or SerialStatus.Returned or SerialStatus.Scrapped,

            SerialStatus.Returned => next is SerialStatus.InStock or SerialStatus.InRepair
                or SerialStatus.Scrapped or SerialStatus.ReturnedToSupplier,

            // Terminal. A written-off unit that could come back would let a shop conjure stock from a
            // write-off, which is precisely the fraud a serial ledger is supposed to make impossible.
            SerialStatus.Scrapped => false,
            SerialStatus.ReturnedToSupplier => false,

            _ => false
        };

        if (!allowed)
        {
            throw new DomainException(
                $"Serial {SerialNumber} is {Status} and cannot become {next}.");
        }

        Status = next;
        WarehouseId = warehouseId;
    }
}

/// <summary>
/// One line of a serial's history: purchased → received → sold → repaired → returned (requirements §18).
///
/// Append-only, like the stock ledger, and for the same reason — this is the evidence behind a
/// warranty decision, and evidence you can edit is not evidence.
/// </summary>
public class SerialEvent : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }

    public Guid SerialId { get; set; }
    public Serial Serial { get; set; } = null!;

    public SerialEventType Type { get; set; }

    /// <summary>The status the unit landed in. Denormalised so the history reads without replaying it.</summary>
    public SerialStatus Status { get; set; }

    public Guid? WarehouseId { get; set; }

    public StockReferenceType? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset At { get; set; }
}

public enum SerialEventType : short
{
    Received = 1,
    Reserved = 2,
    ReservationReleased = 3,
    Sold = 4,
    Returned = 5,
    TransferredOut = 6,
    TransferredIn = 7,
    SentToRepair = 8,
    ReturnedFromRepair = 9,
    Adjusted = 10,
    Scrapped = 11,
    Counted = 12
}
