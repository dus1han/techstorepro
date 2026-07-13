using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Inventory;

/// <summary>
/// Why stock was written on or off. A free-text reason is also required; this is the reportable part.
///
/// Requirements §19 asks for stock adjustments without saying what they are for. In a computer shop
/// they are for exactly this list, and the list is an enum rather than a string because "how much did
/// we lose to damage this year?" is a question the business will ask.
/// </summary>
public enum AdjustmentReason : short
{
    /// <summary>Stock that existed before the system did.</summary>
    OpeningStock = 1,

    Damaged = 2,
    Lost = 3,
    Theft = 4,
    Expired = 5,

    /// <summary>Used internally — a cable for the workshop bench, a machine for the counter.</summary>
    InternalUse = 6,

    /// <summary>Given away: a sample, a demo unit, a goodwill replacement.</summary>
    Sample = 7,

    /// <summary>A miscount, a mis-keyed receipt — the ledger was wrong and is being told the truth.</summary>
    DataCorrection = 8,

    Other = 9
}

/// <summary>
/// A deliberate write-on or write-off of stock (requirements §19).
///
/// Lines carry a signed quantity: positive puts stock on, negative takes it off. One document can do
/// both — a count that found three of one product and lost two of another is one event, and splitting
/// it into two documents would lose that.
///
/// <b>It posts immediately.</b> Unlike a stock count, an adjustment has no approval step: the person
/// who can see the shelf is the person recording it, and requirements only put approval behind counts
/// (§21). The permission to create one is the control — plus the mandatory reason, which is what makes
/// the write-off report worth reading.
/// </summary>
public class StockAdjustment : TenantEntity
{
    public string Number { get; set; } = null!;

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public AdjustmentReason Reason { get; set; }

    /// <summary>Mandatory. "Other" with no explanation is how a write-off report becomes useless.</summary>
    public string Explanation { get; set; } = null!;

    public DateTimeOffset AdjustedAt { get; set; }

    /// <summary>Set when an approved stock count raised this adjustment, rather than a person.</summary>
    public Guid? StockCountId { get; set; }

    public ICollection<StockAdjustmentLine> Lines { get; set; } = [];

    /// <summary>The money written on (positive) or off (negative). The number the business cares about.</summary>
    public decimal NetValue => Lines.Sum(l => l.Quantity * l.UnitCost);

    public void Validate()
    {
        if (Lines.Count == 0)
        {
            throw new DomainException("An adjustment must have at least one line.");
        }

        if (Lines.Any(l => l.Quantity == 0))
        {
            throw new DomainException("An adjustment line of zero units adjusts nothing.");
        }

        if (string.IsNullOrWhiteSpace(Explanation))
        {
            throw new DomainException("An adjustment must say why: stock does not vanish for no reason.");
        }
    }
}

public class StockAdjustmentLine : TenantEntity
{
    public Guid StockAdjustmentId { get; set; }
    public StockAdjustment StockAdjustment { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid? SerialId { get; set; }
    public Serial? Serial { get; set; }

    /// <summary>Signed: positive writes stock on, negative writes it off.</summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// What the adjusted units were valued at. On a write-off it is the warehouse's average — you lose
    /// what the stock was worth, not what you wish it had been worth. On a write-on it is the cost the
    /// user supplied, which then raises the average, so an opening-stock adjustment with a wrong cost
    /// mis-values every future sale of that product.
    /// </summary>
    public decimal UnitCost { get; set; }

    public string? Notes { get; set; }

    public bool IsWriteOn => Quantity > 0;
}
