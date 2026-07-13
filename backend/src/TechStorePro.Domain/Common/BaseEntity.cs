namespace TechStorePro.Domain.Common;

/// <summary>
/// Root type for every persisted entity. Identifiers are GUIDs so that rows can be
/// generated client-side and merged across companies without collisions.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}
