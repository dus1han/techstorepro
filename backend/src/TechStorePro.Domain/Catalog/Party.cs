using TechStorePro.Domain.Common;

namespace TechStorePro.Domain.Catalog;

/// <summary>Requirements §14.</summary>
public enum CustomerType : short
{
    /// <summary>No account, no credit — the counter sale that walks in and pays cash.</summary>
    WalkIn = 1,
    Individual = 2,
    Corporate = 3
}

/// <summary>Requirements §15.</summary>
public enum SupplierType : short
{
    Local = 1,

    /// <summary>Foreign currency, freight, customs — the import flow of requirements §26.</summary>
    Overseas = 2,

    /// <summary>A third party we send repairs out to (requirements §29).</summary>
    RepairVendor = 3
}

public class Customer : TenantEntity
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;

    public CustomerType Type { get; set; } = CustomerType.Individual;

    /// <summary>The trading name, when the customer is a company rather than a person.</summary>
    public string? CompanyName { get; set; }

    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? TaxNumber { get; set; }

    /// <summary>
    /// The most they may owe at once. Zero means cash only — which is the right default for a
    /// walk-in, and the reason credit is opt-in rather than opt-out.
    /// </summary>
    public decimal CreditLimit { get; set; }

    /// <summary>Days to pay. 0 = due on receipt.</summary>
    public int PaymentTermDays { get; set; }

    /// <summary>Which price list they buy at — retail, wholesale, corporate (requirements §31).</summary>
    public Guid? PriceTierId { get; set; }
    public PriceTier? PriceTier { get; set; }

    /// <summary>
    /// What they currently owe. Maintained by the sales and payments modules in P5, not by hand —
    /// it is a cache of the ledger, and P7's receivables report must be able to prove it.
    /// </summary>
    public decimal Balance { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Would this order push them past their credit limit?</summary>
    public bool WouldExceedCreditLimit(decimal orderTotal) =>
        CreditLimit > 0 && Balance + orderTotal > CreditLimit;
}

public class Supplier : TenantEntity
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;

    public SupplierType Type { get; set; } = SupplierType.Local;

    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Country { get; set; }
    public string? TaxNumber { get; set; }

    /// <summary>
    /// ISO 4217. An overseas supplier invoices in their currency, and the FX rate on the day of
    /// receipt is what lands in inventory — see the landed-cost flow in P4.
    /// </summary>
    public string DefaultCurrency { get; set; } = "AED";

    public int PaymentTermDays { get; set; }

    /// <summary>Days from order to arrival. Drives the reorder suggestions in P3.</summary>
    public int LeadTimeDays { get; set; }

    public decimal Balance { get; set; }

    public bool IsActive { get; set; } = true;
}
