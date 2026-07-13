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

    /// <summary>
    /// The username from the token — <c>ahmed</c>, not <c>ahmed@GULF01</c> and not an email address.
    /// This is what the audit trail records. Email is optional and non-unique on a user, so it cannot
    /// answer "who did this".
    /// </summary>
    string? Username { get; }

    bool IsAuthenticated { get; }

    /// <summary>
    /// True only for a TechStorePro platform operator, read from a claim that only the platform login
    /// can mint. Never inferred from the <em>absence</em> of a company: a null tenant switches the
    /// query filters off, so "no company" must mean "refused", not "sees everything".
    /// </summary>
    bool IsPlatformAdmin { get; }

    /// <summary>Caller's IP, recorded on every audit row and login attempt (requirements §8, §9).</summary>
    string? IpAddress { get; }

    string? UserAgent { get; }
}
