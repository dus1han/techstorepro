using TechStorePro.Application.Common.Interfaces;

namespace TechStorePro.API.Services;

/// <summary>
/// Resolves the active company from the <c>company_id</c> claim on the caller's token.
///
/// The claim is the only accepted source. A header or query parameter would let any
/// authenticated user read another company's data by editing the request, so a user who
/// belongs to several companies must obtain a new token to switch between them.
///
/// <para>It also implements <see cref="ITenantSetter"/> for background work, which has no token to
/// read. That path is deliberately one-way: it refuses to run once a claim has supplied a company, so
/// nothing reachable from a request can use it to become another tenant.</para>
/// </summary>
public class TenantContext : ITenantContext, ITenantSetter
{
    public const string CompanyIdClaim = "company_id";

    private readonly Guid? _fromToken;
    private Guid? _fromBackgroundJob;

    public TenantContext(IHttpContextAccessor accessor)
    {
        var claim = accessor.HttpContext?.User.FindFirst(CompanyIdClaim)?.Value;
        _fromToken = Guid.TryParse(claim, out var id) ? id : null;
    }

    public Guid? CompanyId => _fromToken ?? _fromBackgroundJob;

    public bool HasTenant => CompanyId.HasValue;

    public void UseCompany(Guid companyId)
    {
        if (_fromToken is not null)
        {
            // A request already said who it is. If this could overwrite that, it would be a
            // tenant-switching primitive reachable from the outside — the exact hole every query
            // filter in the system exists to close.
            throw new InvalidOperationException(
                "This scope already has a company from the caller's token and cannot be reassigned.");
        }

        _fromBackgroundJob = companyId;
    }
}
