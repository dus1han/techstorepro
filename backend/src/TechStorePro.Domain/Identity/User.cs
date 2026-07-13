using TechStorePro.Domain.Common;
using TechStorePro.Domain.Exceptions;

namespace TechStorePro.Domain.Identity;

/// <summary>
/// A person who works for one company, and logs in as <c>username@COMPANYCODE</c>.
///
/// <b>A user belongs to exactly one company.</b> That is a deliberate reversal of the original design,
/// in which one person could hold memberships in several companies and switch between them. The reason
/// is <see cref="Username"/>: it is unique only <em>within</em> a company, because a shop wants to call
/// its manager "admin" without being told that an invisible stranger already took the name. Once
/// "admin" means different people in different companies, a bare username identifies nobody, and the
/// login has to name its company. So the company is half the login — and a person who genuinely works
/// for two companies has two accounts, which is also the honest description of what they are.
///
/// It follows that the user row is tenant-scoped, and the old <c>CompanyUser</c> join is gone:
/// membership, ownership, branch access and permissions all hang directly here.
///
/// <para><b>The platform admin is not one of these.</b> They live in <see cref="PlatformAdmin"/> — a
/// separate table with a separate login — so that no column on this row can promote a shop's counter
/// clerk into someone who can read every company in the database.</para>
/// </summary>
public class User : TenantEntity
{
    /// <summary>The company they work for. There is exactly one, and it is half of their login.</summary>
    public Company Company { get; set; } = null!;

    /// <summary>
    /// The half of the login before the <c>@</c>. Unique per company, typed by whoever created the
    /// user, and deliberately not an email address — a counter clerk in a shop may not have one.
    ///
    /// It may not contain an <c>@</c>. That is not fussiness: <c>ahmed@GULF01</c> is split on the
    /// <c>@</c> to find the company, and a username containing one would make that split ambiguous.
    /// </summary>
    public string Username { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;
    public string FullName { get; set; } = null!;

    /// <summary>
    /// Optional, and <b>not</b> unique. It is no longer how anyone signs in — it is a contact address,
    /// used for a password-reset link when there is one. A shop assistant with no email still gets an
    /// account, and an admin resets their password for them.
    /// </summary>
    public string? Email { get; set; }

    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The company's first user, created by the platform admin alongside the company itself. An owner
    /// implicitly holds every permission and cannot be locked out of the permission screen — otherwise
    /// a company could revoke its own last administrator and permanently brick itself.
    /// </summary>
    public bool IsOwner { get; set; }

    // --- Failed-login protection (requirements §8) ------------------------------------------
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public bool MustChangePassword { get; set; }

    /// <summary>Branches this user may work in. Empty = every branch of their company.</summary>
    public ICollection<UserBranch> BranchAccess { get; set; } = [];

    public ICollection<UserPermission> Permissions { get; set; } = [];

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

    /// <summary>
    /// Canonical form of a username: trimmed, lower-cased, and refused if blank or containing an
    /// <c>@</c>. Lower-casing means "Ahmed" and "ahmed" are one account rather than two people who
    /// cannot work out why one of them cannot log in.
    /// </summary>
    public static string NormaliseUsername(string username)
    {
        var normalised = username.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(normalised))
        {
            throw new DomainException("A username cannot be blank.");
        }

        if (normalised.Contains('@'))
        {
            throw new DomainException(
                "A username cannot contain '@'. The login 'name@COMPANY' is split on the '@' to find "
                + "the company, so a username containing one could not be told apart from the code.");
        }

        return normalised;
    }
}

/// <summary>
/// Restricts a user to specific branches (requirements §6 "Branch access").
///
/// A join row: hard-deleted rather than retired, for the same reason as <see cref="UserPermission"/> —
/// re-granting branch access must not collide with a soft-deleted row still holding the unique key.
/// </summary>
public class UserBranch : AuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
}
