using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TechStorePro.API.Controllers;

/// <summary>
/// Base for every feature controller: authenticated by default, routed under /api, and
/// wired to MediatR so controllers stay thin dispatchers.
///
/// <b>The policy is <see cref="AuthorizationPolicies.Tenant"/>, not a bare <c>[Authorize]</c>.</b>
/// That is load-bearing. A platform operator's token carries no <c>company_id</c>, and a null tenant
/// switches the DbContext query filters off — so a merely-authenticated platform token reaching one of
/// these controllers would read every company on the platform at once. The policy demands the company
/// claim, so such a token is refused at the door.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.Tenant)]
[Route("api/[controller]")]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _mediator;

    protected ISender Mediator =>
        _mediator ??= HttpContext.RequestServices.GetRequiredService<ISender>();
}

/// <summary>
/// Base for the TechStorePro platform console (requirements §2): onboarding companies, suspending
/// them, listing them. Reachable only by a <see cref="AuthorizationPolicies.Platform"/> token, which
/// only the platform login can mint.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.Platform)]
[Produces("application/json")]
public abstract class PlatformControllerBase : ControllerBase
{
    private ISender? _mediator;

    protected ISender Mediator =>
        _mediator ??= HttpContext.RequestServices.GetRequiredService<ISender>();
}

public static class AuthorizationPolicies
{
    /// <summary>A user of exactly one company. Requires the <c>company_id</c> claim.</summary>
    public const string Tenant = "Tenant";

    /// <summary>A TechStorePro operator. Requires the <c>platform_admin</c> claim.</summary>
    public const string Platform = "Platform";
}
