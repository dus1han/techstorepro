namespace TechStorePro.Application.Common.Interfaces;

/// <summary>
/// The company the current request is acting on behalf of. Resolved once per request
/// from the caller's token and consumed by the DbContext query filters.
/// </summary>
public interface ITenantContext
{
    /// <summary>Null only for unauthenticated or cross-tenant platform-admin requests.</summary>
    Guid? CompanyId { get; }

    bool HasTenant { get; }
}
