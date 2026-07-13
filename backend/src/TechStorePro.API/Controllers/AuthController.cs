using TechStorePro.Application.Identity.Commands.Login;
using TechStorePro.Application.Identity.Commands.Logout;
using TechStorePro.Application.Identity.Commands.RefreshSession;
using TechStorePro.Application.Identity.Commands.Register;
using TechStorePro.Application.Identity.Commands.SwitchCompany;
using TechStorePro.Application.Identity.Dtos;
using TechStorePro.Application.Identity.Queries.GetCurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TechStorePro.API.Controllers;

[Route("api/v1/auth")]
public class AuthController : ApiControllerBase
{
    /// <summary>Registers a company and its first owner (requirements §3).</summary>
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthResult>> Register(RegisterCommand command) =>
        Ok(await Mediator.Send(command));

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResult>> Login(LoginCommand command) =>
        Ok(await Mediator.Send(command));

    /// <summary>Rotates the refresh token. The presented token is revoked on use.</summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResult>> Refresh(RefreshSessionCommand command) =>
        Ok(await Mediator.Send(command));

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutCommand command)
    {
        await Mediator.Send(command);
        return NoContent();
    }

    /// <summary>
    /// Re-issues the session against another company. Switching is an auth operation, never a
    /// request parameter — see api-design.md §2.
    /// </summary>
    [HttpPost("switch-company")]
    public async Task<ActionResult<AuthResult>> SwitchCompany(SwitchCompanyCommand command) =>
        Ok(await Mediator.Send(command));

    /// <summary>The caller, their active company, and every permission they hold there.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserDto>> Me() =>
        Ok(await Mediator.Send(new GetCurrentUserQuery()));
}
