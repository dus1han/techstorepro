using TechStorePro.Domain.Common;

namespace TechStorePro.Domain.Identity;

/// <summary>
/// The tenant. Everything in the ERP hangs off exactly one of these, and the company on the
/// caller's token is what the DbContext filters every query by.
/// </summary>
public class Company : AuditableEntity, ISoftDeletable
{
    public string Name { get; set; } = null!;
    public string? LegalName { get; set; }
    public string? TaxNumber { get; set; }
    public string? RegistrationNumber { get; set; }

    /// <summary>ISO 4217. Every amount in this company's books is ultimately expressed here.</summary>
    public string BaseCurrency { get; set; } = "AED";

    /// <summary>IANA zone, e.g. "Asia/Dubai". Used to decide what "today" means for this company.</summary>
    public string TimeZone { get; set; } = "UTC";

    public string? Country { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? BankDetails { get; set; }
    public string? LogoStorageKey { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public string? DeletedReason { get; set; }

    public ICollection<Branch> Branches { get; set; } = [];
    public ICollection<Warehouse> Warehouses { get; set; } = [];
    public ICollection<CompanyUser> Members { get; set; } = [];
}
