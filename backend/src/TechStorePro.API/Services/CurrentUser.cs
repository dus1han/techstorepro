using System.Security.Claims;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Infrastructure.Identity;

namespace TechStorePro.API.Services;

public class CurrentUser : ICurrentUser
{
    private readonly HttpContext? _context;

    public CurrentUser(IHttpContextAccessor accessor)
    {
        _context = accessor.HttpContext;
    }

    private ClaimsPrincipal? Principal => _context?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>The username from the token — not an email. This is what the audit trail records.</summary>
    public string? Username => Principal?.FindFirstValue(TokenService.UsernameClaim);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    /// <summary>
    /// Asserted by a claim, never inferred from the absence of a company. A token with no
    /// <c>company_id</c> leaves the tenant null, which switches the DbContext query filters off — so
    /// "no company" has to mean "refused", not "sees everything".
    /// </summary>
    public bool IsPlatformAdmin =>
        string.Equals(
            Principal?.FindFirstValue(TokenService.PlatformAdminClaim),
            "true",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Recorded on every audit row and login attempt. Behind a reverse proxy this is the proxy's
    /// address unless ForwardedHeaders is configured — worth remembering before treating it as
    /// evidence of where a change came from.
    /// </summary>
    public string? IpAddress => _context?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var value = _context?.Request.Headers.UserAgent.ToString();

            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            // Bounded: the column is 500 chars, and a hostile client can send a header far longer.
            return value.Length > 500 ? value[..500] : value;
        }
    }
}
