using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;

namespace TechStorePro.Domain.Catalog;

/// <summary>
/// A tax rate, effective-dated (requirements §11).
///
/// <b>The rate is never referenced by a document at read time.</b> A sale copies the percentage onto
/// its line — see the <c>tax_percent_snapshot</c> columns in database-design.md. Changing this row
/// next year must not restate every invoice ever raised, and a foreign key alone cannot promise that.
/// </summary>
public class TaxRate : TenantEntity
{
    public string Name { get; set; } = null!;

    /// <summary>5 means 5%. Stored as a percentage, not a fraction — it is what the user types.</summary>
    public decimal Percent { get; set; }

    public bool IsDefault { get; set; }

    public DateTimeOffset ValidFrom { get; set; }

    /// <summary>Null = still in force.</summary>
    public DateTimeOffset? ValidTo { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsInForceAt(DateTimeOffset at) =>
        IsActive && ValidFrom <= at && (ValidTo is null || ValidTo > at);

    public void Validate()
    {
        if (Percent is < 0 or > 100)
        {
            throw new DomainException("A tax rate must be between 0 and 100 percent.");
        }

        if (ValidTo is { } end && end <= ValidFrom)
        {
            throw new DomainException("A tax rate's validity must end after it begins.");
        }
    }
}

/// <summary>Retail, wholesale, corporate — the customer bands of requirements §31.</summary>
public class PriceTier : TenantEntity
{
    public string Name { get; set; } = null!;

    /// <summary>The tier a customer falls into when none is set on them.</summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<PriceList> PriceLists { get; set; } = [];
}

/// <summary>
/// A set of prices for one tier, valid over a period. Two lists for the same tier may not overlap —
/// otherwise "what does this customer pay today?" would have two answers, and the system would pick
/// one arbitrarily.
/// </summary>
public class PriceList : TenantEntity
{
    public string Name { get; set; } = null!;

    public Guid PriceTierId { get; set; }
    public PriceTier PriceTier { get; set; } = null!;

    public string CurrencyCode { get; set; } = "AED";

    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<PriceListItem> Items { get; set; } = [];

    public bool IsInForceAt(DateTimeOffset at) =>
        IsActive && ValidFrom <= at && (ValidTo is null || ValidTo > at);

    /// <summary>Do this list's dates overlap another's? Half-open intervals: [from, to).</summary>
    public bool OverlapsWith(PriceList other)
    {
        var startsBeforeOtherEnds = other.ValidTo is null || ValidFrom < other.ValidTo;
        var endsAfterOtherStarts = ValidTo is null || ValidTo > other.ValidFrom;

        return startsBeforeOtherEnds && endsAfterOtherStarts;
    }
}

public class PriceListItem : TenantEntity
{
    public Guid PriceListId { get; set; }
    public PriceList PriceList { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal UnitPrice { get; set; }

    /// <summary>Below this, the line needs approval — the discount floor of requirements §32.</summary>
    public decimal? MinimumPrice { get; set; }
}

/// <summary>
/// Requirements §31 asks for price history. It is a separate append-only table rather than an audit
/// query, because "show me this product's price over the last year" is a report the business runs,
/// not a forensic question — and it should not require reading the audit log.
/// </summary>
public class PriceHistory : TenantEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Which price changed: purchase or selling.</summary>
    public PriceKind Kind { get; set; }

    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
    public Guid? ChangedBy { get; set; }
}

public enum PriceKind : short
{
    Purchase = 1,
    Selling = 2
}

public enum DiscountMethod : short
{
    Percentage = 1,
    FixedAmount = 2
}

/// <summary>
/// A discount rule (requirements §32). Applies to a product, a customer, or both.
///
/// <see cref="MaxValue"/> is the ceiling a salesperson may apply without approval — the "discount
/// limits / manager approval" of §32. The approval workflow itself lands with sales in P5; this is
/// the rule it will consult.
/// </summary>
public class Discount : TenantEntity
{
    public string Name { get; set; } = null!;

    /// <summary>Null = applies to every product.</summary>
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>Null = applies to every customer.</summary>
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public DiscountMethod Method { get; set; } = DiscountMethod.Percentage;

    public decimal Value { get; set; }

    /// <summary>Beyond this, a manager must approve. Null = no ceiling.</summary>
    public decimal? MaxValue { get; set; }

    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsInForceAt(DateTimeOffset at) =>
        IsActive && ValidFrom <= at && (ValidTo is null || ValidTo > at);

    public void Validate()
    {
        if (Value < 0)
        {
            throw new DomainException("A discount cannot be negative.");
        }

        if (Method == DiscountMethod.Percentage && Value > 100)
        {
            throw new DomainException("A percentage discount cannot exceed 100%.");
        }

        if (MaxValue is { } max && max < Value)
        {
            throw new DomainException(
                "The approval ceiling cannot be below the discount itself — that would require "
                + "approval for every use of the rule.");
        }
    }

    /// <summary>Does applying this discount need a manager's approval?</summary>
    public bool RequiresApproval(decimal appliedValue) =>
        MaxValue is { } max && appliedValue > max;

    /// <summary>The money taken off a line of <paramref name="lineTotal"/>.</summary>
    public decimal AmountOff(decimal lineTotal) =>
        Method == DiscountMethod.Percentage
            ? Math.Round(lineTotal * Value / 100m, 4)
            : Math.Min(Value, lineTotal);   // a fixed discount can never exceed the line
}
