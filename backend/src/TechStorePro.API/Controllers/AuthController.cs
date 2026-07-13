using TechStorePro.Application.Identity.Commands.Login;
using TechStorePro.Application.Identity.Commands.Logout;
using TechStorePro.Application.Identity.Commands.RefreshSession;
using TechStorePro.Application.Identity.Dtos;
using TechStorePro.Application.Identity.Queries.GetCurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TechStorePro.API.Controllers;

/// <summary>
/// Signing in as a user of a company.
///
/// <b>There is no registration endpoint.</b> A company cannot bring itself into existence: it is
/// onboarded by TechStorePro through <c>POST /api/v1/platform/companies</c>, which creates the company
/// and its first user together. Requirements §2 describes exactly that, and it settles the self-service
/// half of open question Q7.
///
/// <b>And there is no switch-company endpoint.</b> A user belongs to one company, so there is nothing
/// left to switch to.
/// </summary>
[Route("api/v1/auth")]
public class AuthController : ApiControllerBase
{
    /// <summary>
    /// Signs in with <c>username@COMPANY</c> and a password — one field, not two. A separate company
    /// box would ask the user to know something they cannot discover, and a company dropdown would show
    /// every tenant on the platform to anyone who opened the page.
    /// </summary>
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

    /// <summary>The caller, their company, and every permission they hold in it.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserDto>> Me() =>
        Ok(await Mediator.Send(new GetCurrentUserQuery()));
}
