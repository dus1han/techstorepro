using TechStorePro.Domain.Common;

namespace TechStorePro.Domain.Identity;

public enum WarehouseType : short
{
    Main = 1,
    Shop = 2,
    Repair = 3,
    Returns = 4,
    Faulty = 5,
    Transit = 6
}

/// <summary>
/// Where stock physically sits. Every stock balance is keyed by warehouse, never by branch.
///
/// A warehouse is either <b>branch-owned</b> (<see cref="BranchId"/> set — only that branch may use
/// it) or <b>company-shared</b> (<see cref="BranchId"/> null — usable by the branches listed in
/// <see cref="AccessibleToBranches"/>). Requirements demand both, so the distinction is a nullable
/// column rather than two tables.
///
/// "Shared" deliberately does not mean "every branch may drain it": access is granted explicitly,
/// per branch, and separately for issuing and receiving.
/// </summary>
public class Warehouse : TenantEntity
{
    public string Name { get; set; } = null!;
    public string Code { get; set; } = null!;
    public WarehouseType Type { get; set; } = WarehouseType.Main;

    /// <summary>Null = shared at company level. Set = owned by, and private to, that branch.</summary>
    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<BranchWarehouse> AccessibleToBranches { get; set; } = [];

    public bool IsShared => BranchId is null;

    /// <summary>
    /// Whether <paramref name="branchId"/> may move stock through this warehouse. A branch-owned
    /// warehouse answers yes only to its owner; a shared one answers from its access list.
    /// This is the check <c>IStockLedger</c> makes — feature code never reasons about it.
    /// </summary>
    public bool IsAccessibleTo(Guid branchId, bool forIssue) =>
        BranchId is { } owner
            ? owner == branchId
            : AccessibleToBranches.Any(a =>
                a.BranchId == branchId && (forIssue ? a.CanIssue : a.CanReceive));
}

/// <summary>
/// Grants one branch the right to use one company-shared warehouse.
///
/// A join row: hard-deleted, not retired, so that revoking and re-granting access does not collide
/// with the unique index on (branch, warehouse). The audit log keeps the history.
/// </summary>
public class BranchWarehouse : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    /// <summary>May this branch take stock out of the warehouse?</summary>
    public bool CanIssue { get; set; } = true;

    /// <summary>May this branch put stock into it?</summary>
    public bool CanReceive { get; set; } = true;
}
