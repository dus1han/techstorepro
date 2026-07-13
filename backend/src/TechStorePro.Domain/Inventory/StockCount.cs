using TechStorePro.Domain.Catalog;
using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;
using TechStorePro.Domain.Identity;

namespace TechStorePro.Domain.Inventory;

public enum StockCountStatus : short
{
    /// <summary>Open on a scanner. Lines are being added; nothing is committed.</summary>
    Counting = 1,

    /// <summary>Counting finished, variances calculated, waiting for a manager (requirements §21).</summary>
    PendingApproval = 2,

    /// <summary>Approved. The variance has been posted to the ledger as an adjustment.</summary>
    Approved = 3,

    Cancelled = 4
}

/// <summary>
/// A physical stock count (requirements §21): walk the shelves, scan what is there, and reconcile it
/// with what the system believes.
///
/// <b>The system quantity is snapshotted onto the line when the line is counted</b>, not read at
/// approval time. A count that took two hours while the shop kept trading would otherwise compare
/// this morning's shelf against this afternoon's ledger and invent a variance out of the sales that
/// happened in between.
///
/// Approval is a separate step and a separate permission, because approving a count is authorising a
/// write-off — it is the one place in the module where stock can be created or destroyed at will.
/// </summary>
public class StockCount : TenantEntity
{
    public string Number { get; set; } = null!;

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public StockCountStatus Status { get; set; } = StockCountStatus.Counting;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CountedAt { get; set; }

    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>The adjustment the approval raised. Null until then — and it is the only way stock moves.</summary>
    public Guid? StockAdjustmentId { get; set; }

    public string? Notes { get; set; }

    public ICollection<StockCountLine> Lines { get; set; } = [];

    /// <summary>Lines where the shelf and the system disagree. These are the only ones that post.</summary>
    public IEnumerable<StockCountLine> Variances => Lines.Where(l => l.Variance != 0);

    /// <summary>Net money the count would write on (positive) or off (negative) if approved.</summary>
    public decimal NetVarianceValue => Lines.Sum(l => l.Variance * l.UnitCost);

    public void SubmitForApproval(DateTimeOffset at)
    {
        if (Status != StockCountStatus.Counting)
        {
            throw new DomainException($"A count that is {Status} cannot be submitted.");
        }

        if (Lines.Count == 0)
        {
            throw new DomainException("A count with no lines counts nothing.");
        }

        Status = StockCountStatus.PendingApproval;
        CountedAt = at;
    }

    /// <summary>
    /// Approves the count. The caller must post the variance to the ledger in the same transaction and
    /// hand back the adjustment — this method refuses to be marked approved without one, because a
    /// count that says "approved" while the ledger never moved is the worst possible outcome: the shop
    /// believes it has reconciled and it has not.
    /// </summary>
    public void Approve(Guid? by, DateTimeOffset at, Guid? adjustmentId)
    {
        if (Status != StockCountStatus.PendingApproval)
        {
            throw new DomainException($"A count that is {Status} cannot be approved.");
        }

        if (Variances.Any() && adjustmentId is null)
        {
            throw new DomainException(
                "A count with variances cannot be approved without the adjustment that posts them.");
        }

        Status = StockCountStatus.Approved;
        ApprovedBy = by;
        ApprovedAt = at;
        StockAdjustmentId = adjustmentId;
    }

    public void Cancel()
    {
        if (Status == StockCountStatus.Approved)
        {
            throw new DomainException(
                "An approved count has already moved stock and cannot be cancelled. "
                + "Raise an adjustment to correct it.");
        }

        Status = StockCountStatus.Cancelled;
    }
}

public class StockCountLine : TenantEntity
{
    public Guid StockCountId { get; set; }
    public StockCount StockCount { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid? SerialId { get; set; }
    public Serial? Serial { get; set; }

    /// <summary>What the ledger believed, at the moment this line was counted. See the class remarks.</summary>
    public decimal SystemQuantity { get; set; }

    /// <summary>What was actually on the shelf.</summary>
    public decimal CountedQuantity { get; set; }

    /// <summary>The average cost at the moment of counting — what the variance is worth.</summary>
    public decimal UnitCost { get; set; }

    public string? Notes { get; set; }

    /// <summary>Positive: more on the shelf than the system thought. Negative: stock is missing.</summary>
    public decimal Variance => CountedQuantity - SystemQuantity;

    public decimal VarianceValue => Variance * UnitCost;
}
