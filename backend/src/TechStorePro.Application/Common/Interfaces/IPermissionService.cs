using TechStorePro.Domain.Identity;

namespace TechStorePro.Application.Common.Interfaces;

/// <summary>A single grant, as the API and the frontend see it.</summary>
public record PermissionGrant(string FeatureCode, PermissionAction Action);

/// <summary>
/// Answers "may this member do this?".
///
/// Permissions are read from the database per request, not from the token. Two reasons, both
/// load-bearing: a user with hundreds of grants would bloat every request, and — more importantly —
/// a <b>revoked permission must take effect immediately</b>, not whenever the token happens to
/// refresh. A permission baked into a 30-minute token is a permission you cannot actually revoke.
/// </summary>
public interface IPermissionService
{
    /// <summary>Every grant held by the current user in the current company.</summary>
    Task<IReadOnlyCollection<PermissionGrant>> GetGrantsAsync(CancellationToken cancellationToken = default);

    Task<bool> HasPermissionAsync(
        string featureCode,
        PermissionAction action,
        CancellationToken cancellationToken = default);

    /// <summary>Throws <c>ForbiddenException</c> if the permission is absent.</summary>
    Task DemandAsync(
        string featureCode,
        PermissionAction action,
        CancellationToken cancellationToken = default);

    /// <summary>Drops the cached grants for a member, after their permissions are edited.</summary>
    void InvalidateCache(Guid companyUserId);
}
