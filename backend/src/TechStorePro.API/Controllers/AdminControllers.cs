using TechStorePro.Application.Common.Models;
using TechStorePro.Application.Configuration;
using TechStorePro.Application.Identity.Branches;
using TechStorePro.Application.Identity.Commands.SetPermissions;
using TechStorePro.Application.Identity.Queries.GetPermissionGrid;
using TechStorePro.Application.Identity.Users;
using TechStorePro.Application.Identity.Warehouses;
using Microsoft.AspNetCore.Mvc;

namespace TechStorePro.API.Controllers;

/// <summary>
/// Branches (requirements §5). Every action is gated by a (feature, action) permission declared on
/// the command itself — the controller only dispatches.
/// </summary>
[Route("api/v1/branches")]
public class BranchesController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<BranchDto>>> List([FromQuery] GetBranchesQuery query) =>
        Ok(await Mediator.Send(query));

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateBranchCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(List), new { id }, id);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateBranchCommand command)
    {
        if (id != command.Id)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }

    /// <summary>Soft delete. A reason is required (requirements §10).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] string reason)
    {
        await Mediator.Send(new DeleteBranchCommand(id, reason));
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id)
    {
        await Mediator.Send(new RestoreBranchCommand(id));
        return NoContent();
    }
}

/// <summary>Warehouses — branch-owned or company-shared (requirements §19, architecture D2).</summary>
[Route("api/v1/warehouses")]
public class WarehousesController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<WarehouseDto>>> List() =>
        Ok(await Mediator.Send(new GetWarehousesQuery()));

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateWarehouseCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(List), new { id }, id);
    }

    /// <summary>Sets which branches may use a company-shared warehouse, and how.</summary>
    [HttpPut("{id:guid}/access")]
    public async Task<IActionResult> SetAccess(Guid id, SetWarehouseAccessCommand command)
    {
        if (id != command.WarehouseId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }
}

/// <summary>Users and the per-user permission grid (requirements §6, §7).</summary>
[Route("api/v1/users")]
public class UsersController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<CompanyUserDto>>> List() =>
        Ok(await Mediator.Send(new GetCompanyUsersQuery()));

    [HttpPost]
    public async Task<ActionResult<Guid>> Invite(InviteUserCommand command)
    {
        var id = await Mediator.Send(command);
        return CreatedAtAction(nameof(List), new { id }, id);
    }

    /// <summary>The permission matrix for one member: every feature × every action.</summary>
    [HttpGet("{companyUserId:guid}/permissions")]
    public async Task<ActionResult<PermissionGridDto>> GetPermissions(Guid companyUserId) =>
        Ok(await Mediator.Send(new GetPermissionGridQuery(companyUserId)));

    /// <summary>Replaces the member's grants with exactly the set supplied.</summary>
    [HttpPut("{companyUserId:guid}/permissions")]
    public async Task<IActionResult> SetPermissions(Guid companyUserId, SetPermissionsCommand command)
    {
        if (companyUserId != command.CompanyUserId)
        {
            return BadRequest("Route id and body id differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }
}

/// <summary>Effective-dated configuration (requirements §11).</summary>
[Route("api/v1/settings")]
public class SettingsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<SettingDto>>> List() =>
        Ok(await Mediator.Send(new GetSettingsQuery()));

    /// <summary>Writes a new version of the setting. Values in force in the past are never rewritten.</summary>
    [HttpPut("{key}")]
    public async Task<IActionResult> Update(string key, UpdateSettingCommand command)
    {
        if (!string.Equals(key, command.Key, StringComparison.Ordinal))
        {
            return BadRequest("Route key and body key differ.");
        }

        await Mediator.Send(command);
        return NoContent();
    }
}
