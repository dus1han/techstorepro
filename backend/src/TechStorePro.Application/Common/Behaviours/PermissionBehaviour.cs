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

        foreach (var permission in required)
        {
            await _permissions.DemandAsync(permission.Feature, permission.Action, cancellationToken);
        }

        return await next();
    }
}
