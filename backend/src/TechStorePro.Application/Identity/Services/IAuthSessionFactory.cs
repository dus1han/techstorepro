using TechStorePro.Application.Identity.Dtos;

namespace TechStorePro.Application.Identity.Services;

/// <summary>
/// Mints a signed-in session for a user acting in a company: an access token, a stored refresh
/// token, and the profile the frontend needs.
///
/// Login, refresh and switch-company all end here. Keeping it in one place means the refresh-token
/// lifetime, the rotation rules and the claim set cannot drift apart between the three paths — a
/// drift that would show up as "logging in is secure, but switching company quietly isn't".
/// </summary>
public interface IAuthSessionFactory
{
    Task<AuthResult> IssueAsync(
        Guid userId,
        Guid companyId,
        CancellationToken cancellationToken = default);
}
