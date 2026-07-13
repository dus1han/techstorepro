using System.Reflection;
using TechStorePro.Application.Common.Exceptions;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Application.Common.Security;
using MediatR;

namespace TechStorePro.Application.Common.Behaviours;

/// <summary>
/// Enforces the (feature, action) permissions of requirements §7 on every request that declares
/// them with <see cref="RequiresPermissionAttribute"/>.
///
/// This runs in the pipeline rather than in each handler so that a handler cannot forget it. The
/// frontend also hides what a user cannot do, but that is cosmetic — this is the check that counts,
/// because a hidden button is still a reachable HTTP endpoint.
///
/// Several attributes on one request are ANDed: the caller needs all of them.
/// </summary>
public class PermissionBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IPermissionService _permissions;
    private readonly ICurrentUser _currentUser;

    public PermissionBehaviour(IPermissionService permissions, ICurrentUser currentUser)
    {
        _permissions = permissions;
        _currentUser = currentUser;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Platform-only requests first, and they are exclusive: a request that demands a platform
        // operator is never also satisfiable by a company's permission grid. A tenant user reaching
        // one of these is refused here even if the HTTP-level policy were somehow misconfigured — two
        // independent gates, because what is behind them is every company on the platform.
        if (typeof(TRequest).GetCustomAttribute<RequiresPlatformAdminAttribute>(inherit: true) is not null)
        {
            if (!_currentUser.IsAuthenticated || !_currentUser.IsPlatformAdmin)
            {
                throw new ForbiddenException("This operation is restricted to TechStorePro platform administrators.");
            }

            return await next();
        }

        var required = typeof(TRequest)
            .GetCustomAttributes<RequiresPermissionAttribute>(inherit: true)
            .ToArray();

        if (required.Length == 0)
        {
            return await next();
        }

        if (!_currentUser.IsAuthenticated)
        {
            throw new UnauthorizedAccessException("Authentication is required.");
        }

        // A platform operator is not a super-user inside a shop. They have no company, so they have no
        // permission grid, and every tenant-scoped query they issued would run with the filters off.
        // Refusing here means the only way into a company's data is a login belonging to that company.
        if (_currentUser.IsPlatformAdmin)
        {
            throw new ForbiddenException(
                "A platform administrator cannot act inside a company. Sign in as a user of that company.");
        }

        foreach (var permission in required)
        {
            await _permissions.DemandAsync(permission.Feature, permission.Action, cancellationToken);
        }

        return await next();
    }
}
