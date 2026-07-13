using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Identity.Queries.GetPermissionGrid;

/// <summary>
/// The permission screen for one user: every feature, every action it supports, and whether this user
/// holds it. This is the whole of requirements §7's UI contract.
/// </summary>
/// <remarks>
/// The <c>[RequiresPermission]</c> below was missing, which meant any authenticated user could read any
/// colleague's permission grid — a map of who in the company can approve a discount or write off stock,
/// handed to whoever asked. It is a read, so it is gated on View rather than Edit.
/// </remarks>
[RequiresPermission(FeatureCatalog.Permissions, PermissionAction.View)]
public record GetPermissionGridQuery(Guid UserId) : IRequest<PermissionGridDto>;

public record PermissionGridDto(
    Guid UserId,
    string UserFullName,
    string Username,
    bool IsOwner,
    IReadOnlyCollection<PermissionGridFeatureDto> Features);

public record PermissionGridFeatureDto(
    string Feature,
    string Module,
    string Name,
    int DisplayOrder,
    IReadOnlyCollection<PermissionGridActionDto> Actions);

public record PermissionGridActionDto(PermissionAction Action, bool Supported, bool Granted);

public class GetPermissionGridQueryHandler : IRequestHandler<GetPermissionGridQuery, PermissionGridDto>
{
    private readonly IApplicationDbContext _db;

    public GetPermissionGridQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PermissionGridDto> Handle(
        GetPermissionGridQuery request,
        CancellationToken cancellationToken)
    {
        // Tenant-filtered: asking for a user of another company gets a 404, not a 403. A 403 would
        // confirm the id is real (api-design.md §4).
        var user = await _db.Users
            .Include(u => u.Permissions)
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        var held = user.Permissions
            .Where(p => p.Granted)
            .Select(p => (p.FeatureCode, p.Action))
            .ToHashSet();

        var features = FeatureCatalog.All
            .OrderBy(f => f.Module)
            .ThenBy(f => f.DisplayOrder)
            .Select(f => new PermissionGridFeatureDto(
                f.Code,
                f.Module,
                f.Name,
                f.DisplayOrder,
                Enum.GetValues<PermissionAction>()
                    .Select(action => new PermissionGridActionDto(
                        action,
                        // The grid renders every action, but only supported ones are tickable —
                        // granting Approve on a feature with no approval step would be a permission
                        // nothing could ever check.
                        Supported: f.SupportedActions.Contains(action),
                        // The owner holds everything implicitly, so the grid shows it as fully ticked
                        // even though no rows exist. Anything else would be a lie.
                        Granted: user.IsOwner
                            ? f.SupportedActions.Contains(action)
                            : held.Contains((f.Code, action))))
                    .ToList()))
            .ToList();

        return new PermissionGridDto(
            user.Id,
            user.FullName,
            user.Username,
            user.IsOwner,
            features);
    }
}
