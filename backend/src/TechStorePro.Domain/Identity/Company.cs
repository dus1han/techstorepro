using TechStorePro.Domain.Common;

namespace TechStorePro.Domain.Identity;

/// <summary>
/// The tenant. Everything in the ERP hangs off exactly one of these, and the company on the
/// caller's token is what the DbContext filters every query by.
/// </summary>
public class Company : AuditableEntity, ISoftDeletable
{
    public string Name { get; set; } = null!;

    /// <summary>
    /// The company's short code, and the half of a login that says <em>which</em> company: a user signs
    /// in as <c>ahmed@GULF01</c>. Unique across the platform, because it is what disambiguates a
    /// username that is only unique <em>within</em> a company — two shops may both have an "admin".
    ///
    /// Set by the platform admin when the company is created and never by the company itself: it is
    /// half of every one of that company's logins, and letting a tenant rename it would lock its own
    /// staff out.
    /// </summary>
    public string Code { get; set; } = null!;

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
    public ICollection<User> Users { get; set; } = [];

    /// <summary>
    /// Normalises a code to its canonical form. Codes are compared and stored upper-case so that
    /// <c>ahmed@gulf01</c> and <c>ahmed@GULF01</c> are the same login rather than one working and the
    /// other failing for a reason nobody can see.
    /// </summary>
    public static string NormaliseCode(string code) => code.Trim().ToUpperInvariant();
}
