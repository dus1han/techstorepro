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
/// Marks a request as reachable without authentication — login and refresh. Everything else is
/// authenticated by default, so forgetting an attribute fails <em>closed</em>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AllowAnonymousRequestAttribute : Attribute;

/// <summary>
/// Declares that a request may only be executed by a TechStorePro platform operator — onboarding a
/// company, suspending one, listing them all.
///
/// It is <b>not</b> the same thing as "has no company". A platform admin's token carries no
/// <c>company_id</c>, and this codebase treats a null tenant as "query filters off" — which is right
/// for a migration and would be a catastrophe for a request. So platform access is asserted
/// positively, by a claim that only the platform login can mint, and never inferred from something
/// being absent.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequiresPlatformAdminAttribute : Attribute;
