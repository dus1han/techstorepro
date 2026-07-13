namespace TechStorePro.Domain.Common;

/// <summary>
/// The base class most business entities will derive from: audited, soft-deletable and
/// owned by a single company.
/// </summary>
public abstract class TenantEntity : AuditableEntity, ITenantScoped, ISoftDeletable
{
    public Guid CompanyId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public string? DeletedReason { get; set; }
}
