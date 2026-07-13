using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TechStorePro.API.Controllers;

/// <summary>
/// Base for every feature controller: authenticated by default, routed under /api, and
/// wired to MediatR so controllers stay thin dispatchers.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _mediator;

    protected ISender Mediator =>
        _mediator ??= HttpContext.RequestServices.GetRequiredService<ISender>();
}
