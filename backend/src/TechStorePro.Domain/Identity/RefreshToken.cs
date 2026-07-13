using TechStorePro.Domain.Common;

namespace TechStorePro.Domain.Identity;

/// <summary>
/// A rotating refresh token for a tenant user.
///
/// It no longer carries a company of its own. It used to, because a user could hold memberships in
/// several companies and the token had to remember which one the session was for. A user now belongs
/// to exactly one company, so the company is <c>User.CompanyId</c> — and a copy of it here would be a
/// second answer to the same question, free to drift from the first.
///
/// Only the <b>hash</b> is stored. A database dump must not hand the reader a set of working
/// credentials, and a refresh token is exactly that.
/// </summary>
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string TokenHash { get; set; } = null!;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Set when this token is rotated, pointing at the token that replaced it. If a <em>revoked</em>
    /// token is presented, its whole chain is suspect — that is a replayed token, not a mistake —
    /// and every descendant is revoked.
    /// </summary>
    public Guid? ReplacedByTokenId { get; set; }

    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
