using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;

namespace TechStorePro.Domain.Catalog;

/// <summary>What the line actually is. Requirements §16 sells all three.</summary>
public enum ProductKind : short
{
    Product = 1,
    Service = 2,
    SparePart = 3
}

/// <summary>Requirements §16: "Product Type — selectable: Brand New, Refurbished".</summary>
public enum ProductCondition : short
{
    BrandNew = 1,
    Refurbished = 2
}

/// <summary>
/// How a unit is identified in stock. This is the single most consequential field on a product,
/// because it decides what the stock ledger has to carry for it in P3.
/// </summary>
public enum TrackingMode : short
{
    /// <summary>Fungible. A cable is a cable; you count them.</summary>
    None = 1,

    /// <summary>
    /// Each unit has a serial number and is individually tracked from receipt to sale to repair.
    /// This is what makes a warranty claim answerable two years later (requirements §18, §30).
    /// </summary>
    Serial = 2,

    /// <summary>Tracked in lots — batteries, thermal paste, anything with an expiry.</summary>
    Batch = 3
}

public class ProductCategory : TenantEntity
{
    public string Name { get; set; } = null!;

    /// <summary>Self-referencing: "Laptops → Gaming → 17-inch".</summary>
    public Guid? ParentId { get; set; }
    public ProductCategory? Parent { get; set; }

    public ICollection<ProductCategory> Children { get; set; } = [];

    public bool IsActive { get; set; } = true;
}

public class Brand : TenantEntity
{
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// A product, a service or a spare part (requirements §16).
///
/// Prices here are the <em>defaults</em>. The price a customer actually pays is resolved from their
/// price tier at the moment of sale and then <b>snapshotted onto the invoice line</b> — editing this
/// field next year must not restate last year's invoices.
/// </summary>
public class Product : TenantEntity
{
    public string ItemCode { get; set; } = null!;
    public string Sku { get; set; } = null!;

    /// <summary>The scanned barcode. Distinct from the SKU: the manufacturer chooses one, we choose the other.</summary>
    public string? Barcode { get; set; }

    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    public Guid? CategoryId { get; set; }
    public ProductCategory? Category { get; set; }

    public Guid? BrandId { get; set; }
    public Brand? Brand { get; set; }

    public string? Model { get; set; }

    /// <summary>Free-form specs (RAM, CPU, screen size). JSON, because every category wants different keys.</summary>
    public string? Specifications { get; set; }

    public ProductKind Kind { get; set; } = ProductKind.Product;
    public ProductCondition Condition { get; set; } = ProductCondition.BrandNew;
    public TrackingMode TrackingMode { get; set; } = TrackingMode.None;

    /// <summary>"each", "box", "metre".</summary>
    public string Unit { get; set; } = "each";

    public decimal PurchasePrice { get; set; }
    public decimal SellingPrice { get; set; }

    public Guid? TaxRateId { get; set; }
    public TaxRate? TaxRate { get; set; }

    /// <summary>Months of shop warranty offered at sale. Zero means none.</summary>
    public int WarrantyMonths { get; set; }

    /// <summary>Below this, the product shows up in the low-stock report (requirements §36).</summary>
    public decimal ReorderLevel { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// A service has no physical units, so it cannot be serial-tracked and cannot have a reorder
    /// level — a stock ledger entry for "one hour of labour" is meaningless. Enforced here rather
    /// than in a validator so that no code path can create one.
    /// </summary>
    public void Validate()
    {
        if (Kind == ProductKind.Service && TrackingMode != TrackingMode.None)
        {
            throw new DomainException(
                "A service cannot be serial- or batch-tracked: it has no physical units to track.");
        }

        if (SellingPrice < 0 || PurchasePrice < 0)
        {
            throw new DomainException("Prices cannot be negative.");
        }
    }

    /// <summary>
    /// Margin on the default prices. Negative is legal and is exactly what the business wants to
    /// see — selling below cost is a decision, not an error, but it must be visible.
    /// </summary>
    public decimal? DefaultMarginPercent =>
        SellingPrice == 0 ? null : (SellingPrice - PurchasePrice) / SellingPrice * 100m;
}
