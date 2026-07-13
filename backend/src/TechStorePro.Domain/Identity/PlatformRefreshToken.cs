using TechStorePro.Domain.Common;

namespace TechStorePro.Domain.Identity;

/// <summary>
/// A rotating refresh token for a <see cref="PlatformAdmin"/>, with the same rotation and
/// replay-detection rules as the tenant <see cref="RefreshToken"/> — a replayed token kills its whole
/// chain.
///
/// It is a separate table for the same reason the admin is: a platform session is not a company
/// session with a flag set. Sharing one table would mean one nullable <c>company_id</c> and one
/// nullable <c>user_id</c>, and a bug that left both null would mint a token belonging to nobody and
/// filtered by nothing.
///
/// Only the <b>hash</b> is stored. A database dump must not hand the reader a working credential —
/// and here it would be a working credential to <em>every</em> company at once.
/// </summary>
public class PlatformRefreshToken : BaseEntity
{
    public Guid PlatformAdminId { get; set; }
    public PlatformAdmin PlatformAdmin { get; set; } = null!;

    public string TokenHash { get; set; } = null!;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Set when this token is rotated, pointing at the token that replaced it. If a <em>revoked</em>
    /// token is presented, that is a replay rather than a mistake, and every descendant is revoked.
    /// </summary>
    public Guid? ReplacedByTokenId { get; set; }

    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
