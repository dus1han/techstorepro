using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Inventory;

/// <summary>
/// Why stock moved. The type carries its own direction, so no caller ever decides the sign of a
/// quantity — <see cref="MovementTypes.Direction"/> does, once, for everybody.
///
/// Requirements §19 asks a historical stock report to break down into opening / purchases / sales /
/// transfers / adjustments / repairs / closing. Those buckets are exactly these values grouped, which
/// is why the enum is finer-grained than "in" and "out".
/// </summary>
public enum MovementType : short
{
    /// <summary>The stock that existed before the system did. Raised once, per product, per warehouse.</summary>
    OpeningBalance = 1,

    /// <summary>A goods receipt (P4). Raises the moving average with the landed unit cost.</summary>
    Receipt = 2,

    /// <summary>Sold and delivered (P5).</summary>
    Sale = 3,

    /// <summary>A customer brought it back (P5).</summary>
    SaleReturn = 4,

    /// <summary>Sent back to the supplier (P4).</summary>
    PurchaseReturn = 5,

    TransferOut = 6,
    TransferIn = 7,

    AdjustmentIn = 8,
    AdjustmentOut = 9,

    /// <summary>A part consumed by a repair job (P6).</summary>
    RepairConsumption = 10,

    /// <summary>A part booked back in from a repair that did not need it (P6).</summary>
    RepairReturn = 11,

    /// <summary>The variance an approved physical count wrote off. Positive or negative.</summary>
    CountAdjustmentIn = 12,
    CountAdjustmentOut = 13
}

/// <summary>What document caused the movement. The pair (type, id) is the audit trail back to it.</summary>
public enum StockReferenceType : short
{
    Opening = 1,
    GoodsReceipt = 2,
    Invoice = 3,
    Delivery = 4,
    CreditNote = 5,
    StockTransfer = 6,
    StockAdjustment = 7,
    StockCount = 8,
    RepairTicket = 9,
    PurchaseReturn = 10
}

public static class MovementTypes
{
    /// <summary>
    /// +1 raises stock, −1 lowers it. The single place in the system that knows the sign of a
    /// movement type: a handler that got this backwards would silently invert a warehouse.
    /// </summary>
    public static int Direction(this MovementType type) => type switch
    {
        MovementType.OpeningBalance => +1,
        MovementType.Receipt => +1,
        MovementType.SaleReturn => +1,
        MovementType.TransferIn => +1,
        MovementType.AdjustmentIn => +1,
        MovementType.RepairReturn => +1,
        MovementType.CountAdjustmentIn => +1,

        MovementType.Sale => -1,
        MovementType.PurchaseReturn => -1,
        MovementType.TransferOut => -1,
        MovementType.AdjustmentOut => -1,
        MovementType.RepairConsumption => -1,
        MovementType.CountAdjustmentOut => -1,

        _ => throw new DomainException($"Movement type {type} has no defined direction.")
    };

    public static bool IsInbound(this MovementType type) => type.Direction() > 0;
    public static bool IsOutbound(this MovementType type) => type.Direction() < 0;

    /// <summary>
    /// Must this movement be told what the stock cost, or may it take the warehouse's current average?
    ///
    /// <b>Required</b> where the stock is arriving from outside this warehouse's average and therefore
    /// carries a value of its own:
    ///
    /// <list type="bullet">
    /// <item><b>Receipt</b> and <b>OpeningBalance</b> — a purchase. Its price is the whole input to the
    ///   moving average, and defaulting it to the existing average would make a receipt at a new price
    ///   change nothing at all.</item>
    /// <item><b>AdjustmentIn</b> — stock written on. It came from somewhere; somebody has to say what it
    ///   was worth.</item>
    /// <item><b>TransferIn</b> — the source warehouse's average, captured when the van was loaded. Take
    ///   the <em>destination's</em> average instead and a transfer would silently revalue the stock,
    ///   which means a company could raise its own inventory value by shuttling a van back and forth.</item>
    /// </list>
    ///
    /// Everything else — stock back from a repair, a count surplus, a customer return — is valued at
    /// what this warehouse already believes the product is worth, unless the caller knows better and
    /// says so. Inventing a fresh cost for stock that was never bought would move the average for no
    /// economic reason, and with a moving average that error never washes out.
    /// </summary>
    public static bool RequiresUnitCost(this MovementType type) =>
        type is MovementType.OpeningBalance
            or MovementType.Receipt
            or MovementType.AdjustmentIn
            or MovementType.TransferIn;
}

/// <summary>
/// One line of the stock ledger: <b>append-only, never updated, never deleted</b>.
///
/// This is the source of truth for every quantity and every cost in the system. <see cref="StockBalance"/>
/// is a cache of it, written in the same transaction, and a nightly job proves the two still agree.
/// A correction is a new, opposing movement — not an edit — which is why this entity is deliberately
/// <em>not</em> soft-deletable: a ledger you can retire a row from is not a ledger.
/// </summary>
public class StockMovement : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }

    /// <summary>Stock is keyed by warehouse. The branch rides along for reporting only (database-design.md §3).</summary>
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Set only for a serial-tracked product, where every movement is exactly one unit.</summary>
    public Guid? SerialId { get; set; }
    public Serial? Serial { get; set; }

    public MovementType Type { get; set; }

    /// <summary>
    /// Signed: positive raises the warehouse, negative lowers it. Stored signed rather than as a
    /// magnitude plus a direction flag so that "the balance is the sum of the movements" is a plain
    /// <c>SUM(quantity)</c> — the recompute that audits the cache must not itself need business logic.
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// The unit cost this movement was valued at. On an inbound purchase it is the landed cost that
    /// raised the average; on an outbound it is the average at the instant of issue — the COGS a sale
    /// snapshots. Never null: a movement with no cost is a hole in the valuation report.
    /// </summary>
    public decimal UnitCost { get; set; }

    /// <summary>The moving average <em>after</em> this movement was applied. Stored so that a
    /// valuation as of a past date can be read straight off the ledger rather than replayed.</summary>
    public decimal AverageCostAfter { get; set; }

    /// <summary>The balance after this movement. Same reason: a historical stock report is a lookup.</summary>
    public decimal BalanceAfter { get; set; }

    public StockReferenceType ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }

    /// <summary>The human-readable document number ("ADJ-2026-00007"), so a report needs no join.</summary>
    public string? ReferenceNumber { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// When the stock actually moved, which is not always when the row was written — a receipt can be
    /// backdated to the day the van arrived. Historical stock is replayed on this, not on created_at.
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; }

    public decimal Value => Quantity * UnitCost;
}
