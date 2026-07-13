using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using TechStorePro.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Identity.Queries.GetPermissionGrid;

/// <summary>
/// The permission screen for one member: every feature, every action it supports, and whether this
/// member holds it. This is the whole of requirements §7's UI contract.
/// </summary>
public record GetPermissionGridQuery(Guid CompanyUserId) : IRequest<PermissionGridDto>;

public record PermissionGridDto(
    Guid CompanyUserId,
    string UserFullName,
    string UserEmail,
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
        // Tenant-filtered: asking for a member of another company gets a 404, not a 403. A 403 would
        // confirm the id is real (api-design.md §4).
        var member = await _db.CompanyUsers
            .Include(m => m.User)
            .Include(m => m.Permissions)
            .FirstOrDefaultAsync(m => m.Id == request.CompanyUserId, cancellationToken)
            ?? throw new NotFoundException("Member", request.CompanyUserId);

        var held = member.Permissions
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
                        // The owner holds everything implicitly, so the grid shows it as fully
                        // ticked even though no rows exist. Anything else would be a lie.
                        Granted: member.IsOwner
                            ? f.SupportedActions.Contains(action)
                            : held.Contains((f.Code, action))))
                    .ToList()))
            .ToList();

        return new PermissionGridDto(
            member.Id,
            member.User.FullName,
            member.User.Email,
            member.IsOwner,
            features);
    }
}
