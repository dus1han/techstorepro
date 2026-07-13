using TechStorePro.Domain.Common;

namespace TechStorePro.Domain.Identity;

/// <summary>
/// A person. Deliberately <b>not</b> tenant-scoped: one login, many companies. The join to a tenant
/// is <see cref="CompanyUser"/>, and that is where permissions hang.
///
/// The consequence, which is a deliberate trade: an email address is unique across the whole
/// <em>platform</em>, not per company. Two companies cannot independently own "info@shop.ae" — they
/// share the one person behind it.
/// </summary>
public class User : AuditableEntity, ISoftDeletable
{
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;

    // --- Failed-login protection (requirements §8) ------------------------------------------
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public bool MustChangePassword { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public string? DeletedReason { get; set; }

    public ICollection<CompanyUser> Memberships { get; set; } = [];

    public bool IsLockedOut(DateTimeOffset now) => LockedUntil is { } until && until > now;

    /// <summary>
    /// Records a failed attempt and locks the account once the threshold is reached. Both the
    /// threshold and the lockout duration come from Settings — requirements §11 forbids hardcoding
    /// a password policy.
    /// </summary>
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
