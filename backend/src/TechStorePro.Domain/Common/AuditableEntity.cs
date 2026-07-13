namespace TechStorePro.Domain.Common;

/// <summary>
/// An entity whose creation and last modification are tracked. The audit fields are
/// populated by the persistence layer on SaveChanges, never by hand.
/// </summary>
public abstract class AuditableEntity : BaseEntity
{
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
