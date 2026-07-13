using TechStorePro.Domain.Identity;

namespace TechStorePro.Application.Common.Security;

/// <summary>
/// Declares the permission a request needs. Put it on the command or query itself, not on the
/// controller: the check then travels with the operation and cannot be lost by a new caller
/// dispatching the same command from somewhere else.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequiresPermissionAttribute : Attribute
{
    public RequiresPermissionAttribute(string feature, PermissionAction action)
    {
        Feature = feature;
        Action = action;
    }

    public string Feature { get; }
    public PermissionAction Action { get; }
}

/// <summary>
/// Marks a request as reachable without authentication — registration, login, refresh. Everything
/// else is authenticated by default, so forgetting an attribute fails <em>closed</em>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AllowAnonymousRequestAttribute : Attribute;
