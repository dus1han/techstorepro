using System.Security.Claims;
using TechStorePro.Application.Common.Interfaces;

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

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

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
