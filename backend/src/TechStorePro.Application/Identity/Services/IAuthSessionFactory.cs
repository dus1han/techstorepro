using TechStorePro.Application.Identity.Dtos;

namespace TechStorePro.Application.Identity.Services;

/// <summary>
/// Mints a signed-in session: an access token, a stored refresh token, and the profile the frontend
/// needs.
///
/// Login and refresh both end here. Keeping it in one place means the refresh-token lifetime, the
/// rotation rules and the claim set cannot drift apart between the two paths — a drift that would show
/// up as "logging in is secure, but refreshing quietly isn't".
///
/// <see cref="IssueAsync"/> takes no company: a user belongs to exactly one, so passing it in would
/// only create the opportunity to pass the wrong one.
/// </summary>
public interface IAuthSessionFactory
{
    Task<AuthResult> IssueAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A platform operator's session. Separate all the way down: a different table, a different
    /// refresh-token table, and a token carrying no <c>company_id</c> at all.
    /// </summary>
    Task<PlatformAuthResult> IssuePlatformAsync(
        Guid platformAdminId,
        CancellationToken cancellationToken = default);
}
