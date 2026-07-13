using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace TechStorePro.Infrastructure.Identity;

/// <summary>
/// Resolves what the caller may do (requirements §7).
///
/// Grants are read from the database, cached briefly per (user, company), and invalidated whenever
/// a grant changes. They are not carried in the token: a permission baked into a 30-minute access
/// token is a permission that stays live for 30 minutes after you revoke it, which is not a
/// revocation anyone would accept.
/// </summary>
public class PermissionService : IPermissionService
{
    /// <summary>
    /// Short enough that a stale grant cannot survive meaningfully, long enough to spare the
    /// database a lookup on every request of a busy POS session. Explicit invalidation
    /// (<see cref="InvalidateCache"/>) is the primary mechanism; this TTL is the backstop for the
    /// case where a grant is changed by another process.
    /// </summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenant;
    private readonly IMemoryCache _cache;

    public PermissionService(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        ITenantContext tenant,
        IMemoryCache cache)
    {
        _db = db;
        _currentUser = currentUser;
        _tenant = tenant;
        _cache = cache;
    }

    public async Task<IReadOnlyCollection<PermissionGrant>> GetGrantsAsync(
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId || _tenant.CompanyId is not { } companyId)
        {
            return [];
        }

        // Tenant-filtered, so this can only ever find a user of the company on the caller's token. A
        // user id from one company presented against another's tenant simply does not resolve.
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.Id == userId && u.CompanyId == companyId && u.IsActive,
                cancellationToken);

        if (user is null)
        {
            return [];
        }

        // The owner holds everything, implicitly and unconditionally.
        //
        // Without this, a company could revoke the permission to manage permissions from its last
        // administrator and lock itself out of its own tenant with no way back in short of a database
        // edit. The owner flag is set once, when the platform onboards the company, and is the floor
        // under that.
        if (user.IsOwner)
        {
            return FeatureCatalog.All
                .SelectMany(f => f.SupportedActions.Select(a => new PermissionGrant(f.Code, a)))
                .ToList();
        }

        var key = CacheKey(user.Id);

        if (_cache.TryGetValue(key, out IReadOnlyCollection<PermissionGrant>? cached) && cached is not null)
        {
            return cached;
        }

        var grants = await _db.UserPermissions
            .Where(p => p.UserId == user.Id && p.Granted)
            .Select(p => new PermissionGrant(p.FeatureCode, p.Action))
            .ToListAsync(cancellationToken);

        _cache.Set(key, (IReadOnlyCollection<PermissionGrant>)grants, CacheFor);

        return grants;
    }

    public async Task<bool> HasPermissionAsync(
        string featureCode,
        PermissionAction action,
        CancellationToken cancellationToken = default)
    {
        var grants = await GetGrantsAsync(cancellationToken);

        return grants.Any(g => g.FeatureCode == featureCode && g.Action == action);
    }

    public async Task DemandAsync(
        string featureCode,
        PermissionAction action,
        CancellationToken cancellationToken = default)
    {
        if (!await HasPermissionAsync(featureCode, action, cancellationToken))
        {
            throw new ForbiddenException(featureCode, action.ToString());
        }
    }

    public void InvalidateCache(Guid userId) => _cache.Remove(CacheKey(userId));

    private static string CacheKey(Guid userId) => $"permissions:{userId}";
}
