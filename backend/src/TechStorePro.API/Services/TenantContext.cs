using TechStorePro.Application.Common.Interfaces;

namespace TechStorePro.API.Services;

/// <summary>
/// Resolves the active company from the <c>company_id</c> claim on the caller's token.
///
/// The claim is the only accepted source. A header or query parameter would let any
/// authenticated user read another company's data by editing the request, so a user who
/// belongs to several companies must obtain a new token to switch between them.
/// </summary>
public class TenantContext : ITenantContext
{
    public const string CompanyIdClaim = "company_id";

    public TenantContext(IHttpContextAccessor accessor)
    {
        var claim = accessor.HttpContext?.User.FindFirst(CompanyIdClaim)?.Value;
        CompanyId = Guid.TryParse(claim, out var id) ? id : null;
    }

    public Guid? CompanyId { get; }

    public bool HasTenant => CompanyId.HasValue;
}
