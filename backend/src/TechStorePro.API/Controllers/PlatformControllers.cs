using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Identity.Dtos;
using TechStorePro.Application.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TechStorePro.API.Controllers;

/// <summary>
/// The platform operator's login. Separate from <c>/api/v1/auth</c> all the way down: a different
/// table, a different refresh-token table, and a token with no company in it.
/// </summary>
[Route("api/v1/platform/auth")]
[AllowAnonymous]
[ApiController]
[Produces("application/json")]
public class PlatformAuthController : ControllerBase
{
    private readonly MediatR.ISender _mediator;

    public PlatformAuthController(MediatR.ISender mediator)
    {
        _mediator = mediator;
    }

    /// <summary>A bare username — no <c>@company</c>. That absence is what makes this a platform login.</summary>
    [HttpPost("login")]
    public async Task<ActionResult<PlatformAuthResult>> Login(PlatformLoginCommand command) =>
        Ok(await _mediator.Send(command));
}

/// <summary>
/// The TechStorePro admin console (requirements §2): the companies on the platform, and the onboarding
/// of new ones.
///
/// This is the only place in the system that reads across tenants, and every action on it demands the
/// platform claim — twice, in fact: once at the HTTP policy on <see cref="PlatformControllerBase"/>,
/// and again in the MediatR pipeline from the <c>[RequiresPlatformAdmin]</c> on each request. Two gates,
/// because what is behind them is every company's data.
/// </summary>
[Route("api/v1/platform/companies")]
public class PlatformCompaniesController : PlatformControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<CompanySummaryDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null) =>
        Ok(await Mediator.Send(new GetCompaniesQuery(page, pageSize, search)));

    /// <summary>
    /// Onboards a company and its first user together. This is what replaced self-service
    /// registration: a tenant can no longer bring itself into existence.
    ///
    /// Returns the owner's full login — <c>admin@GULF01</c> — because that string is what the operator
    /// reads out to the customer, and it is the one thing they cannot reconstruct from anywhere else.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CompanyCreatedDto>> Create(CreateCompanyCommand command) =>
        Ok(await Mediator.Send(command));

    /// <summary>Suspends a company. Nobody in it can sign in while it is suspended — including its owner.</summary>
    [HttpPost("{id:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid id)
    {
        await Mediator.Send(new SetCompanyActiveCommand(id, IsActive: false));
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id)
    {
        await Mediator.Send(new SetCompanyActiveCommand(id, IsActive: true));
        return NoContent();
    }
}
