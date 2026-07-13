using TechStorePro.Domain.Common;

namespace TechStorePro.Domain.Identity;

/// <summary>
/// A TechStorePro platform operator (requirements §2): the people who onboard companies, not the
/// people who run a shop.
///
/// <b>A separate table, on purpose.</b> The cheap version of this is a boolean on <see cref="User"/>,
/// and it is cheap right up to the day somebody sets it by mistake — a stray mass-update, a seed
/// script, a badly-scoped admin screen — and a shop's counter clerk can read every company in the
/// database. Platform access is not a *degree* of company access; it is a different kind of thing, and
/// it gets its own row, its own password, its own tokens and its own login endpoint. There is no column
/// anywhere on a tenant user that can escalate into this one.
///
/// It is deliberately <em>not</em> <c>ITenantScoped</c>: a platform admin belongs to no company. That
/// is exactly why their token carries no <c>company_id</c> claim, and why every tenant-scoped query
/// they could possibly issue must go through an endpoint that names the company explicitly.
/// </summary>
public class PlatformAdmin : AuditableEntity, ISoftDeletable
{
    /// <summary>
    /// Unique across the platform — there is only one platform, so there is nothing to scope it to.
    /// A platform admin signs in with a bare username, with no <c>@company</c> after it; that absence
    /// is what tells the two login flows apart.
    /// </summary>
    public string Username { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string? Email { get; set; }

    public bool IsActive { get; set; } = true;

    // --- Failed-login protection, same rules as a tenant user ---------------------------------
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public bool MustChangePassword { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public string? DeletedReason { get; set; }

    public ICollection<PlatformRefreshToken> RefreshTokens { get; set; } = [];

    public bool IsLockedOut(DateTimeOffset now) => LockedUntil is { } until && until > now;

    public void RegisterFailedLogin(DateTimeOffset now, int maxAttempts, TimeSpan lockoutFor)
    {
        FailedLoginCount++;

        if (FailedLoginCount >= maxAttempts)
        {
            LockedUntil = now.Add(lockoutFor);
            FailedLoginCount = 0;
        }
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockedUntil = null;
        LastLoginAt = now;
    }
}
