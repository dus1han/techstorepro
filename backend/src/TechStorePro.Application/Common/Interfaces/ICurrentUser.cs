namespace TechStorePro.Application.Common.Interfaces;

/// <summary>
/// The authenticated principal behind the current request, exposed to the Application
/// layer without dragging in HttpContext.
///
/// There is no <c>Roles</c> member, and that is deliberate: requirements §7 forbids fixed roles, so
/// nothing in the system resolves a role name. Authorisation is asked of
/// <see cref="IPermissionService"/> as a (feature, action) question instead.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }

    /// <summary>Caller's IP, recorded on every audit row and login attempt (requirements §8, §9).</summary>
    string? IpAddress { get; }

    string? UserAgent { get; }
}
