namespace TechStorePro.Domain.Common;

/// <summary>
/// Marks an entity as belonging to exactly one company (tenant). Implementing this
/// interface is what makes an entity subject to the global tenant query filter and to
/// automatic CompanyId assignment on insert — a tenant-owned entity that forgets to
/// implement it will silently leak across companies.
/// </summary>
public interface ITenantScoped
{
    Guid CompanyId { get; set; }
}
