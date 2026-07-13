using TechStorePro.Domain.Common;

namespace TechStorePro.Domain.Identity;

/// <summary>
/// A physical location of the company — a shop, a repair centre. Branches do not hold stock;
/// <see cref="Warehouse"/>s do. A branch points at the warehouse it uses by default, which is what
/// requirements §5 means by "Branch details: Default warehouse".
/// </summary>
public class Branch : TenantEntity
{
    public string Name { get; set; } = null!;
    public string Code { get; set; } = null!;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    /// <summary>
    /// Where a transaction at this branch takes stock from unless the user picks otherwise.
    /// Nullable only because a branch is created before its first warehouse exists.
    /// </summary>
    public Guid? DefaultWarehouseId { get; set; }
    public Warehouse? DefaultWarehouse { get; set; }

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Warehouses this branch owns outright.</summary>
    public ICollection<Warehouse> OwnedWarehouses { get; set; } = [];

    /// <summary>Company-shared warehouses this branch has been granted access to.</summary>
    public ICollection<BranchWarehouse> SharedWarehouseAccess { get; set; } = [];
}
