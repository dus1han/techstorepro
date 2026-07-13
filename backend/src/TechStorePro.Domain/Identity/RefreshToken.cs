using TechStorePro.Domain.Common;

namespace TechStorePro.Domain.Identity;

/// <summary>
/// A rotating refresh token, bound to the company it was issued for.
///
/// Only the <b>hash</b> is stored. A database dump must not hand the reader a set of working
/// credentials, and a refresh token is exactly that.
/// </summary>
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>The active company this token re-issues an access token for.</summary>
    public Guid CompanyId { get; set; }

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
