using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Identity.Dtos;
using TechStorePro.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.Application.Identity.Queries.GetCurrentUser;

public record GetCurrentUserQuery : IRequest<CurrentUserDto>;

public class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, CurrentUserDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenant;
    private readonly IPermissionService _permissions;

    public GetCurrentUserQueryHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        ITenantContext tenant,
        IPermissionService permissions)
    {
        _db = db;
        _currentUser = currentUser;
        _tenant = tenant;
        _permissions = permissions;
    }

    public async Task<CurrentUserDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedAccessException("Authentication is required.");

        // Tenant-filtered, unlike before: the user is now scoped to a company, so this can only
        // resolve a user of the company on the caller's own token.
        var user = await _db.Users
            .Include(u => u.Company)
            .Include(u => u.BranchAccess)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        var grants = await _permissions.GetGrantsAsync(cancellationToken);

        // An empty list means "all branches", not "no branches" — see User.BranchAccess. Resolving it
        // here rather than in the client keeps that rule in one place.
        var branchIds = user.BranchAccess.Count > 0
            ? user.BranchAccess.Select(b => b.BranchId).ToList()
            : await _db.Branches
                .Where(b => b.IsActive)
                .Select(b => b.Id)
                .ToListAsync(cancellationToken);

        return new CurrentUserDto(
            user.Id,
            user.Username,
            user.FullName,
            _tenant.CompanyId,
            user.Company.Name,
            user.Company.Code,
            user.IsOwner,
            grants.Select(g => new PermissionDto(g.FeatureCode, g.Action)).ToList(),
            branchIds);
    }
}
